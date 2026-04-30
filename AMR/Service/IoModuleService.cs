using AMR.Communication;
using AMR.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>타워램프 색상</summary>
public enum TowerLampColor
{
    Red,
    Yellow,
    Green
}

/// <summary>
/// LS산전 XEL-BSSRT I/O 모듈 Modbus TCP 통신 서비스 —
/// 자동 연결/재연결 + 입력 모니터링 + 램프/버저/서보 제어.
/// </summary>
public class IoModuleService : BackgroundService
{
    private readonly IoModuleModbusTcpClient _modbusClient;
    private readonly CobotService _cobotService;
    private readonly ILogger<IoModuleService> _logger;

    private IoModuleInputStatus? _currentInputs;
    private bool _lastEmoState;
    private bool _lastResetState;

    // 리셋 스위치 짧게/길게 누름 구분 상태
    private DateTime? _resetPressedAt;
    private bool _longPressTriggered;
    private volatile bool _isHandlingReset;
    private volatile bool _isHandlingToggle;

    private const int CobotTimeoutSeconds = 60;
    private const int PollIntervalMs = 500;
    private const int InputPollIntervalMs = 200;       // 메인 입력 폴링 주기 (짧은 펄스 캐치)
    private const int LongPressDurationMs = 5000;       // 5초 이상 누르면 Manual↔Auto 토글

    public IoModuleService(IoModuleModbusTcpClient modbusClient, CobotService cobotService, ILogger<IoModuleService> logger)
    {
        _modbusClient = modbusClient;
        _cobotService = cobotService;
        _logger = logger;
    }

    /// <summary>Modbus TCP 연결 상태</summary>
    public bool IsConnected => _modbusClient.IsConnected;

    /// <summary>가장 최근에 폴링한 입력 상태 (연결 전/폴링 전에는 null)</summary>
    public IoModuleInputStatus? CurrentInputs => _currentInputs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IoModuleService 시작");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!IsConnected)
                {
                    _logger.LogWarning("I/O Module Modbus TCP 연결 시도");
                    await _modbusClient.ConnectAsync(stoppingToken);
                    _logger.LogInformation("I/O Module Modbus TCP 연결 완료");
                }
                else
                {
                    var inputs = await _modbusClient.ReadInputsAsync(stoppingToken);
                    _currentInputs = inputs;

                    if (inputs.Emo && !_lastEmoState)
                        _logger.LogWarning("EMO(비상정지) 활성 감지 — X000 ON");
                    else if (!inputs.Emo && _lastEmoState)
                        _logger.LogInformation("EMO(비상정지) 해제 — X000 OFF");

                    _lastEmoState = inputs.Emo;

                    // 리셋 스위치 처리 (짧게=복구 시퀀스, 5초 이상=Manual↔Auto 토글)
                    HandleResetSwitchInputs(inputs.Reset, stoppingToken);
                    _lastResetState = inputs.Reset;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "I/O Module 통신 실패 — 5초 후 재시도");
            }

            await Task.Delay(IsConnected
                ? TimeSpan.FromMilliseconds(InputPollIntervalMs)
                : TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _modbusClient.DisconnectAsync(cancellationToken);
        _logger.LogInformation("IoModuleService 종료");
        await base.StopAsync(cancellationToken);
    }

    #region 리셋 스위치 처리

    /// <summary>
    /// 매 폴링 사이클마다 호출 — 짧게 누름(OFF→ON→OFF)은 복구 시퀀스,
    /// 5초 이상 누름은 Manual↔Auto 토글로 분기. 시퀀스는 fire-and-forget으로
    /// 백그라운드 실행하여 폴링 루프가 막히지 않도록 한다.
    /// </summary>
    private void HandleResetSwitchInputs(bool resetNow, CancellationToken stoppingToken)
    {
        var now = DateTime.Now;

        // 1) 상승 엣지 — 누르기 시작
        if (resetNow && !_lastResetState)
        {
            _resetPressedAt = now;
            _longPressTriggered = false;
            _logger.LogDebug("리셋 스위치 ON 감지 (X001 OFF→ON)");
            return;
        }

        // 2) 계속 눌림 — 5초 경과 시 롱프레스 트리거 (한 번만)
        if (resetNow && _lastResetState)
        {
            if (!_longPressTriggered &&
                _resetPressedAt is { } pressedAt &&
                (now - pressedAt).TotalMilliseconds >= LongPressDurationMs)
            {
                _longPressTriggered = true;
                _logger.LogInformation("리셋 스위치 5초 이상 길게 누름 감지 — 코봇 Manual↔Auto 토글");

                if (_isHandlingToggle)
                {
                    _logger.LogWarning("이미 Manual↔Auto 토글 진행 중 — 추가 트리거 무시");
                }
                else
                {
                    _isHandlingToggle = true;
                    _ = Task.Run(async () =>
                    {
                        try { await HandleManualAutoToggleAsync(stoppingToken); }
                        finally { _isHandlingToggle = false; }
                    }, stoppingToken);
                }
            }
            return;
        }

        // 3) 하강 엣지 — 손을 뗌
        if (!resetNow && _lastResetState)
        {
            if (_resetPressedAt is { } pressedAt)
            {
                var elapsedMs = (int)(now - pressedAt).TotalMilliseconds;
                _resetPressedAt = null;

                if (_longPressTriggered)
                {
                    _logger.LogInformation("리셋 스위치 떼어짐 (롱프레스 토글 실행 후, {Ms}ms 눌림) — 복구 시퀀스 스킵", elapsedMs);
                    _longPressTriggered = false;
                    return;
                }

                // 짧게 누름 → 기존 복구 시퀀스
                _logger.LogInformation("리셋 스위치 짧게 누름 감지 ({Ms}ms) — 코봇 복구 시퀀스 시작", elapsedMs);

                if (_isHandlingReset)
                {
                    _logger.LogWarning("이미 코봇 복구 시퀀스 진행 중 — 추가 트리거 무시");
                }
                else
                {
                    _isHandlingReset = true;
                    _ = Task.Run(async () =>
                    {
                        try { await HandleResetSwitchAsync(stoppingToken); }
                        finally { _isHandlingReset = false; }
                    }, stoppingToken);
                }
            }
        }
    }

    /// <summary>리셋 스위치 5초 이상 롱프레스 시 코봇 Manual↔Auto 토글</summary>
    private async Task HandleManualAutoToggleAsync(CancellationToken ct)
    {
        try
        {
            // 시각 피드백 — Reset Lamp 빠르게 3회 점멸
            for (var i = 0; i < 3; i++)
            {
                await SetResetSwLampAsync(true, ct);
                await Task.Delay(150, ct);
                await SetResetSwLampAsync(false, ct);
                await Task.Delay(150, ct);
            }

            _logger.LogInformation("[토글] ManualAutoSwitch 실행");
            await _cobotService.ManualAutoSwitchAsync(ct);

            _logger.LogInformation("[토글] Manual↔Auto 토글 완료");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[토글] Manual↔Auto 토글 실패");
        }
    }

    /// <summary>
    /// 리셋 스위치 짧게 누름 시 실행되는 코봇 복구 시퀀스.
    /// 각 단계는 best-effort — 개별 실패가 후속 단계를 막지 않는다.
    /// 5·6 단계는 EnsureAutoAndRunningAsync 한 번으로 묶어서 토글 오작동을 방지.
    /// 순서: 부저OFF → Recovery → ClearAllFaults → Servo ON → Auto+Main → Phome(DI25)
    /// </summary>
    private async Task HandleResetSwitchAsync(CancellationToken ct)
    {
        // 1. 부저 OFF — 알람음 해제
        await TryStepAsync("부저 OFF", () => SetTowerLampBuzzerAsync(false, ct), ct);

        // 2. 코봇 복구
        await TryStepAsync("코봇 복구(Recovery)", () => _cobotService.RecoveryAsync(ct), ct);

        // 3. 전체 오류 해제 (시간이 걸릴 수 있어 1초 대기)
        await TryStepAsync("전체 오류 해제(ClearAllFaults)", async () =>
        {
            await _cobotService.ClearAllFaultsAsync(ct);
            await Task.Delay(1000, ct);
        }, ct);

        // 4. 코봇 활성화 (Servo ON) — 모터 전원 인가
        await TryStepAsync("코봇 활성화(Servo ON)", () => SetCobotServoAsync(true, ct), ct);

        // 5·6. Auto 모드 + Main Program 보장 (현재 상태 확인 후 필요할 때만 토글/시작)
        await TryStepAsync("Auto 모드 + Main Program 보장", () => _cobotService.EnsureAutoAndRunningAsync(ct), ct);

        // 7. 코봇 홈(Phome) 위치 이동 (DI25 핸드셰이크) — 앞 단계가 모두 성공해야 의미 있음
        try
        {
            _logger.LogInformation("[리셋] 코봇 Phome 위치 이동(DI25) 실행");
            await SendCobotCommandAndWaitAsync(25, "Phome 위치 이동", ct);
            _logger.LogInformation("[리셋] 코봇 복구 시퀀스 완료");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[리셋] 코봇 홈 위치 이동 실패");
        }
    }

    /// <summary>리셋 시퀀스의 한 단계를 실행하고 실패해도 다음 단계로 진행</summary>
    private async Task TryStepAsync(string stepName, Func<Task> action, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        _logger.LogInformation("[리셋] {Step} 실행", stepName);
        try
        {
            await action();
            await Task.Delay(500, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[리셋] {Step} 실패 — 다음 단계 계속 진행", stepName);
        }
    }

    /// <summary>Cobot DI 명령 전송 후 DO0(Busy) 확인 → DI OFF → DO1(Complete) 또는 DO2(Error) 대기</summary>
    private async Task SendCobotCommandAndWaitAsync(ushort diIndex, string description, CancellationToken ct)
    {
        if (!_cobotService.IsConnected)
            throw new InvalidOperationException($"Cobot 미연결 상태에서 명령 시도: {description}");

        await _cobotService.WriteDigitalInputAsync(diIndex, true, ct);

        var deadline = DateTime.Now.AddSeconds(CobotTimeoutSeconds);

        try
        {
            // Phase 1: DO0(Busy) 대기
            while (!ct.IsCancellationRequested)
            {
                if (DateTime.Now > deadline)
                    throw new TimeoutException($"Cobot Busy 대기 타임아웃 ({CobotTimeoutSeconds}초): {description}");

                var dos = await _cobotService.ReadDigitalOutputsAsync(0, 3, ct);

                if (dos[2])
                    throw new Exception($"Cobot 에러 발생: {description}");

                if (dos[0])
                    break;

                await Task.Delay(PollIntervalMs, ct);
            }

            ct.ThrowIfCancellationRequested();

            // Busy 확인 → DI OFF
            try
            {
                await _cobotService.WriteDigitalInputAsync(diIndex, false, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Busy 확인 후 DI{DiIndex} OFF 실패", diIndex);
            }

            // Phase 2: DO1(Complete) 또는 DO2(Error) 대기
            while (!ct.IsCancellationRequested)
            {
                if (DateTime.Now > deadline)
                    throw new TimeoutException($"Cobot 완료 대기 타임아웃 ({CobotTimeoutSeconds}초): {description}");

                var dos = await _cobotService.ReadDigitalOutputsAsync(0, 3, ct);

                if (dos[2])
                    throw new Exception($"Cobot 에러 발생: {description}");

                if (dos[1])
                    break;

                await Task.Delay(PollIntervalMs, ct);
            }

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            try
            {
                await _cobotService.WriteDigitalInputAsync(diIndex, false, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DI{DiIndex} OFF 실패", diIndex);
            }
        }
    }

    #endregion

    #region 읽기

    /// <summary>입력(X000~X005) 읽기</summary>
    public Task<IoModuleInputStatus> ReadInputsAsync(CancellationToken ct = default)
        => _modbusClient.ReadInputsAsync(ct);

    /// <summary>출력(Y000~Y005) 현재 상태 읽기</summary>
    public Task<IoModuleOutputStatus> ReadOutputsAsync(CancellationToken ct = default)
        => _modbusClient.ReadOutputsAsync(ct);

    #endregion

    #region 출력 제어

    public Task SetTowerLampRedAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetTowerLampRedAsync(value, ct);

    public Task SetTowerLampYellowAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetTowerLampYellowAsync(value, ct);

    public Task SetTowerLampGreenAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetTowerLampGreenAsync(value, ct);

    public Task SetTowerLampBuzzerAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetTowerLampBuzzerAsync(value, ct);

    public Task SetResetSwLampAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetResetSwLampAsync(value, ct);

    public Task SetCobotServoAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetCobotServoAsync(value, ct);

    /// <summary>타워램프 색상별 편의 제어</summary>
    public Task SetTowerLampAsync(TowerLampColor color, bool value, CancellationToken ct = default)
        => color switch
        {
            TowerLampColor.Red => SetTowerLampRedAsync(value, ct),
            TowerLampColor.Yellow => SetTowerLampYellowAsync(value, ct),
            TowerLampColor.Green => SetTowerLampGreenAsync(value, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(color))
        };

    /// <summary>타워램프 R/Y/G 일괄 OFF</summary>
    public Task AllTowerLampsOffAsync(CancellationToken ct = default)
        => _modbusClient.AllTowerLampsOffAsync(ct);

    #endregion
}
