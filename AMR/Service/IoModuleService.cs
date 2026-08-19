using AMR.Communication;
using AMR.Enums;
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
    private readonly MoveSequenceRunner _sequenceRunner;
    private readonly AmrService _amrService;
    private readonly ILogger<IoModuleService> _logger;

    private IoModuleInputStatus? _currentInputs;
    private bool _lastEmoState;
    private bool _lastResetState;
    private bool _inputsBaselineEstablished;   // 첫 폴링은 baseline 으로만 사용 (콜드 부팅 시 ON 상태를 엣지로 오인식 방지)

    // MzDetect 센서 이전 상태 (Magazine 제거 감지용)
    private bool _lastMzDetect1, _lastMzDetect2, _lastMzDetect3, _lastMzDetect4;
    private bool _mzInitialized;

    // 리셋 스위치 짧게/길게 누름 구분 상태
    private DateTime? _resetPressedAt;
    private bool _longPressTriggered;
    private volatile bool _isHandlingReset;
    private volatile bool _isHandlingToggle;

    private const int CobotTimeoutSeconds = 60;
    private const int PollIntervalMs = 500;
    private const int InputPollIntervalMs = 200;       // 메인 입력 폴링 주기 (짧은 펄스 캐치)
    private const int LongPressDurationMs = 5000;       // 5초 이상 누르면 ClearFaults + Stop + Manual 강제 전환
    private const ushort ResetAmrTaskIndex = 50;        // 리셋(짧게 누름) 복구 시퀀스 마지막에 AMR 으로 보내는 TaskIndex
    private const ushort ResetAmrJobIndex = 1;          // 위 TaskIndex 와 함께 보내는 JobIndex

    public IoModuleService(IoModuleModbusTcpClient modbusClient, CobotService cobotService, MoveSequenceRunner sequenceRunner, AmrService amrService, ILogger<IoModuleService> logger)
    {
        _modbusClient = modbusClient;
        _cobotService = cobotService;
        _sequenceRunner = sequenceRunner;
        _amrService = amrService;
        _logger = logger;
    }

    /// <summary>Modbus TCP 연결 상태</summary>
    public bool IsConnected => _modbusClient.IsConnected;

    /// <summary>가장 최근에 폴링한 입력 상태 (연결 전/폴링 전에는 null)</summary>
    public IoModuleInputStatus? CurrentInputs => _currentInputs;

    /// <summary>현재 활성화된 비정상 상황 (없으면 null)</summary>
    public AbnormalInfo? CurrentAbnormal { get; private set; }

    /// <summary>
    /// 다른 서비스(MoveSequenceRunner 등) 에서 abnormal 을 보고할 수 있는 진입점.
    /// 설정된 값은 다음 MainSequenceService 상태 publish 사이클에 ACS 로 전송된다.
    /// </summary>
    public void SetAbnormal(AbnormalInfo info)
    {
        CurrentAbnormal = info;
        _logger.LogWarning("Abnormal 보고: Type={Type}, Node={Node}", info.Type, info.Node);
    }

    /// <summary>외부에서 abnormal 을 명시적으로 해제</summary>
    public void ClearAbnormal() => CurrentAbnormal = null;

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

                    // 첫 폴링은 baseline 으로만 사용 — 콜드 부팅 시 EMO/Reset이 ON 상태라도
                    // OFF→ON 엣지로 오인식해서 리셋 시퀀스나 EMO 알람을 트리거하지 않도록.
                    if (!_inputsBaselineEstablished)
                    {
                        _lastEmoState = inputs.Emo;
                        _lastResetState = inputs.Reset;
                        _inputsBaselineEstablished = true;
                        _logger.LogInformation(
                            "I/O Module 입력 baseline 초기화 (EMO={Emo}, Reset={Reset})",
                            inputs.Emo, inputs.Reset);

                        // MzDetect 도 첫 호출에서 자체적으로 baseline 처리
                        EvaluateMzDetect(inputs);
                    }
                    else
                    {
                        if (inputs.Emo && !_lastEmoState)
                            _logger.LogWarning("EMO(비상정지) 활성 감지 — X000 ON");
                        else if (!inputs.Emo && _lastEmoState)
                            _logger.LogInformation("EMO(비상정지) 해제 — X000 OFF");

                        _lastEmoState = inputs.Emo;

                        // Magazine 제거 감지 (MzDetect ON→OFF)
                        EvaluateMzDetect(inputs);

                        // 리셋 스위치 처리 (짧게=복구 시퀀스, 5초 이상=Manual↔Auto 토글)
                        HandleResetSwitchInputs(inputs.Reset, stoppingToken);
                        _lastResetState = inputs.Reset;
                    }
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

    #region Magazine 제거 감지

    /// <summary>
    /// MzDetect 센서 ON→OFF 전환 감지 — Cobot이 Magazine을 핸들링하는
    /// 단계(CobotPickup/CobotPlace)가 아닌 상황에서 감지되면 CARRIER_REMOVED abnormal 보고.
    /// 해당 포트 센서가 다시 ON이 되면 abnormal 자동 해제.
    /// </summary>
    private void EvaluateMzDetect(IoModuleInputStatus inputs)
    {
        if (!_mzInitialized)
        {
            _lastMzDetect1 = inputs.MzDetect1;
            _lastMzDetect2 = inputs.MzDetect2;
            _lastMzDetect3 = inputs.MzDetect3;
            _lastMzDetect4 = inputs.MzDetect4;
            _mzInitialized = true;
            return;
        }

        var step = _sequenceRunner.State.CurrentStep;
        bool cobotHandling = step is SequenceStep.CobotPickup or SequenceStep.CobotPlace;

        if (!cobotHandling)
        {
            CheckPortRemoval(1, _lastMzDetect1, inputs.MzDetect1);
            CheckPortRemoval(2, _lastMzDetect2, inputs.MzDetect2);
            CheckPortRemoval(3, _lastMzDetect3, inputs.MzDetect3);
            CheckPortRemoval(4, _lastMzDetect4, inputs.MzDetect4);
        }

        // 제거된 포트의 센서가 다시 ON이면 abnormal 해제
        if (CurrentAbnormal is { } abnormal)
        {
            bool restored = abnormal.Node switch
            {
                "PORT1" => inputs.MzDetect1,
                "PORT2" => inputs.MzDetect2,
                "PORT3" => inputs.MzDetect3,
                "PORT4" => inputs.MzDetect4,
                _ => false
            };

            if (restored)
            {
                _logger.LogInformation("Magazine 복귀 감지 — {Port} (MzDetect OFF→ON) — abnormal 해제", abnormal.Node);
                CurrentAbnormal = null;
            }
        }

        _lastMzDetect1 = inputs.MzDetect1;
        _lastMzDetect2 = inputs.MzDetect2;
        _lastMzDetect3 = inputs.MzDetect3;
        _lastMzDetect4 = inputs.MzDetect4;
    }

    private void CheckPortRemoval(int port, bool prev, bool current)
    {
        // ON→OFF 전환 = Magazine 제거
        if (prev && !current)
        {
            CurrentAbnormal = new AbnormalInfo
            {
                Type = "CARRIER_REMOVED",
                Node = $"PORT{port}",
                Timestamp = DateTime.UtcNow.ToString("o")
            };
            _logger.LogWarning("Magazine 제거 감지 — PORT{Port} (MzDetect ON→OFF)", port);
        }
    }

    #endregion

    #region 리셋 스위치 처리

    /// <summary>
    /// 매 폴링 사이클마다 호출 — 짧게 누름(OFF→ON→OFF)은 복구 시퀀스,
    /// 5초 이상 누름은 코봇 에러 상태에서도 강제로 안전 상태(ClearFaults → Stop → Manual)
    /// 로 전환. 시퀀스는 fire-and-forget으로 백그라운드 실행하여 폴링 루프가 막히지 않도록 한다.
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
                _logger.LogInformation("리셋 스위치 5초 이상 길게 누름 감지 — 에러 해제 + Stop + Manual 강제 전환");

                if (_isHandlingToggle)
                {
                    _logger.LogWarning("이미 Manual 강제 전환 진행 중 — 추가 트리거 무시");
                }
                else
                {
                    _isHandlingToggle = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleManualAutoToggleAsync(stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[Manual전환] fire-and-forget Task 예외");
                        }
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
                        try
                        {
                            await HandleResetSwitchAsync(stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[리셋] fire-and-forget Task 예외 — 시퀀스가 도중에 종료됨");
                        }
                        finally { _isHandlingReset = false; }
                    }, stoppingToken);
                }
            }
        }
    }

    /// <summary>
    /// 리셋 스위치 5초 이상 롱프레스 시 코봇을 강제로 안전 상태로 전환.
    /// 코봇 에러 발생 상태에서도 동작하도록 다음 순서로 처리:
    ///   1) ClearAllFaults — 에러 해제
    ///   2) StopJob — Main Program 정지
    ///   3) Auto 모드면 ManualAutoSwitch — Manual 로 전환 (이미 Manual 이면 스킵)
    /// 각 단계는 best-effort — 개별 실패가 후속 단계를 막지 않는다.
    /// </summary>
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

            // 1. 전체 오류 해제 — 에러 발생 상태에서도 후속 명령이 먹히도록
            try
            {
                _logger.LogInformation("[Manual전환] 전체 오류 해제(ClearAllFaults) 실행");
                await _cobotService.ClearAllFaultsAsync(ct);
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Manual전환] ClearAllFaults 실패 — 다음 단계 계속 진행");
            }

            // 2. Main Program Stop
            try
            {
                _logger.LogInformation("[Manual전환] Main Program Stop 실행");
                await _cobotService.StopJobAsync(ct);
                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Manual전환] StopJob 실패 — 다음 단계 계속 진행");
            }

            // 3. 현재 모드 확인 후 Auto 면 Manual 로 전환 (RobotMode: 0=Auto, 1=Manual)
            try
            {
                var status = await _cobotService.ReadStatusAsync(ct);
                if (status.RobotMode == 0)
                {
                    _logger.LogInformation("[Manual전환] Auto → Manual 전환 (ManualAutoSwitch 실행)");
                    await _cobotService.ManualAutoSwitchAsync(ct);
                }
                else
                {
                    _logger.LogInformation("[Manual전환] 이미 Manual 모드 — 전환 스킵");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Manual전환] Manual 전환 실패");
            }

            // 4. ACS 로 OPERATOR_ABORT abnormal 보고 — ACS 가 현재 진행 중인 job 을 삭제하도록.
            //    SetAbnormal 은 CurrentAbnormal 을 설정하고, MainSequenceService 가 다음 status
            //    publish(1초 주기)에 포함시켜 전송한다. 새 job 이 들어오면 RunSequenceAsync 에서 해제.
            try
            {
                _logger.LogInformation("[Manual전환] ACS 로 OPERATOR_ABORT abnormal 보고 (현재 job 삭제 요청)");
                SetAbnormal(new AbnormalInfo
                {
                    Type = "OPERATOR_ABORT",
                    Node = "AMR",
                    Timestamp = DateTime.UtcNow.ToString("o")
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Manual전환] OPERATOR_ABORT abnormal 보고 실패");
            }

            _logger.LogInformation("[Manual전환] 5초 롱프레스 시퀀스 완료 (ClearFaults + Stop + Manual + OPERATOR_ABORT)");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Manual전환] 5초 롱프레스 시퀀스 실패");
        }
    }

    /// <summary>
    /// 리셋 스위치 짧게 누름 시 실행되는 코봇 복구 시퀀스.
    /// 각 단계는 best-effort — 개별 실패가 후속 단계를 막지 않는다.
    /// 5·6 단계는 EnsureAutoAndRunningAsync 한 번으로 묶어서 토글 오작동을 방지.
    /// 순서: 부저OFF → Recovery → ClearAllFaults → Servo ON → Auto+Main → Phome(DI25) → AMR TASK 50
    /// </summary>
    private async Task HandleResetSwitchAsync(CancellationToken ct)
    {
        _logger.LogInformation("[리셋] 코봇 복구 시퀀스 시작");

        // 0. Faulted 상태 해제 — 경광등이 NORMAL 상태로 복귀하도록
        try
        {
            _sequenceRunner.ClearFault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[리셋] ClearFault 실패 — 다음 단계 계속 진행");
        }

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

        // 5초 롱프레스로 설정된 OPERATOR_ABORT abnormal 해제 — 운전자가 reset 으로 코봇을
        // Auto 로 복귀시켰다는 것은 운전자 개입 상황이 정리됐다는 의미. (이미 ACS 로 전송 완료된 뒤)
        if (CurrentAbnormal?.Type is "OPERATOR_ABORT" or "EXCHANGE_CANCEL_HOLD")
        {
            _logger.LogInformation("[리셋] 코봇 Auto 복귀 — {Type} abnormal 해제", CurrentAbnormal?.Type);
            ClearAbnormal();
        }

        // 7. 코봇 홈(Phome) 위치 이동 (DI25 핸드셰이크) — 앞 단계가 모두 성공해야 의미 있음
        var phomeOk = false;
        try
        {
            _logger.LogInformation("[리셋] 코봇 Phome 위치 이동(DI25) 실행");
            await SendCobotCommandAndWaitAsync(25, "Phome 위치 이동", ct);
            phomeOk = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[리셋] 코봇 홈 위치 이동 실패");
        }

        // 8. 경광등 정상(Green) 복귀 — Phome 까지 성공했을 때만
        if (phomeOk)
        {
            await TryStepAsync("경광등 정상(Green) 복귀", async () =>
            {
                await AllTowerLampsOffAsync(ct);
                await SetTowerLampGreenAsync(true, ct);
            }, ct);

            _logger.LogInformation("[리셋] 코봇 복구 시퀀스 완료");
        }
        else
        {
            _logger.LogWarning("[리셋] Phome 이동 실패 — 경광등은 알람 색 유지");
        }

        // 9. AMR 에 TASK 50 수행 명령 — TaskIndex/JobIndex 설정 후 Start.
        //    (코봇 복구와 무관하게 best-effort 로 전송)
        await TryStepAsync($"AMR TASK {ResetAmrTaskIndex}/Job {ResetAmrJobIndex} 수행", async () =>
        {
            await _amrService.SetTaskIndexAsync(ResetAmrTaskIndex, ct);
            await _amrService.SetJobIndexAsync(ResetAmrJobIndex, ct);
            await _amrService.SetExecutionControlAsync(ExecutionControl.Start, ct);
        }, ct);
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

    #region Public Reset / Toggle (UI에서 호출)

    /// <summary>UI에서 호출하는 리셋 — 물리 리셋 스위치 짧게 누른 것과 동일</summary>
    public Task ResetAsync(CancellationToken ct = default)
        => HandleResetSwitchAsync(ct);

    /// <summary>
    /// UI에서 호출하는 Manual↔Auto 토글.
    /// Manual 전환: Main Program Stop → Manual 전환
    /// Auto 전환: Auto 전환 → Main Program Run
    /// </summary>
    public async Task ManualAutoToggleAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await _cobotService.ReadStatusAsync(ct);
            var isAuto = status.RobotMode == 0;

            if (isAuto)
            {
                // Auto → Manual: Stop → Manual 전환
                _logger.LogInformation("[토글] Auto→Manual: Main Program Stop 실행");
                await _cobotService.StopJobAsync(ct);
                await Task.Delay(500, ct);
                _logger.LogInformation("[토글] Auto→Manual: ManualAutoSwitch 실행");
                await _cobotService.ManualAutoSwitchAsync(ct);
            }
            else
            {
                // Manual → Auto: Auto 전환 → Main Program Run
                _logger.LogInformation("[토글] Manual→Auto: ManualAutoSwitch 실행");
                await _cobotService.ManualAutoSwitchAsync(ct);
                await Task.Delay(500, ct);
                _logger.LogInformation("[토글] Manual→Auto: StartMainProgram 실행");
                await _cobotService.StartMainProgramAsync(ct);
            }

            _logger.LogInformation("[토글] Manual↔Auto 토글 완료");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[토글] Manual↔Auto 토글 실패");
        }
    }

    /// <summary>부저 OFF</summary>
    public Task BuzzOffAsync(CancellationToken ct = default)
        => SetTowerLampBuzzerAsync(false, ct);

    #endregion
}
