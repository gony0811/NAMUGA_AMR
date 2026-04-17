using AMR.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 경광등(Signal Tower) 제어 서비스 — 시스템 상태에 따라 R/O/G 램프 및 버저를 자동 제어.
/// docs/signal_tower.md 운영 조건 기준.
/// </summary>
public class SignalTowerService : BackgroundService
{
    private readonly AmrService _amrService;
    private readonly CobotService _cobotService;
    private readonly IoModuleService _ioModuleService;
    private readonly MoveSequenceRunner _sequenceRunner;
    private readonly ILogger<SignalTowerService> _logger;

    private const int PollIntervalMs = 1000;
    private const float LowBatteryThreshold = 20f;

    public SignalTowerService(
        AmrService amrService,
        CobotService cobotService,
        IoModuleService ioModuleService,
        MoveSequenceRunner sequenceRunner,
        ILogger<SignalTowerService> logger)
    {
        _amrService = amrService;
        _cobotService = cobotService;
        _ioModuleService = ioModuleService;
        _sequenceRunner = sequenceRunner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SignalTowerService 시작");

        // 점멸 토글 상태
        bool blinkToggle = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_ioModuleService.IsConnected)
                {
                    await Task.Delay(PollIntervalMs, stoppingToken);
                    continue;
                }

                var (red, orange, green, buzzer) = EvaluateState(blinkToggle);
                await ApplyAsync(red, orange, green, buzzer, stoppingToken);

                blinkToggle = !blinkToggle;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalTower 제어 실패");
            }

            await Task.Delay(PollIntervalMs, stoppingToken);
        }
    }

    /// <summary>현재 시스템 상태를 평가하여 램프/버저 ON/OFF 결정</summary>
    private (bool Red, bool Orange, bool Green, bool Buzzer) EvaluateState(bool blinkToggle)
    {
        // 우선순위 1: Red — 이상 상태
        // EMO 확인
        var inputs = _ioModuleService.CurrentInputs;
        if (inputs is { Emo: true })
            return (Red: true, Orange: false, Green: false, Buzzer: true);

        // 시퀀스 Faulted
        if (_sequenceRunner.State.CurrentStep == SequenceStep.Faulted)
            return (Red: true, Orange: false, Green: false, Buzzer: true);

        // 통신 끊김 (AMR/Cobot/I/O)
        if (!_amrService.IsConnected || !_cobotService.IsConnected)
            return (Red: true, Orange: false, Green: false, Buzzer: false);

        // 우선순위 2: Orange — 작업 중
        if (_sequenceRunner.State.IsRunning)
            return (Red: false, Orange: false, Green: true, Buzzer: false);

        // 우선순위 3: Green — 정상 대기
        // 배터리 20% 이하 점멸
        bool lowBattery = false;
        try
        {
            var robotStatus = GetCachedRobotStatus();
            if (robotStatus != null && robotStatus.Battery.LevelPercent <= LowBatteryThreshold)
                lowBattery = true;
        }
        catch
        {
            // 배터리 읽기 실패 시 무시
        }

        bool orangeOn = lowBattery ? blinkToggle : true;
        return (Red: false, Orange: orangeOn, Green: true, Buzzer: false);
    }

    /// <summary>
    /// MainSequenceService에서 주기적으로 읽는 상태를 활용하기 위해
    /// AmrService에서 마지막으로 읽은 상태를 조회.
    /// 연결이 안 되어 있으면 null 반환.
    /// </summary>
    private Models.RobotStatus? GetCachedRobotStatus()
    {
        if (!_amrService.IsConnected) return null;
        return _amrService.LastStatus;
    }

    private bool _lastRed, _lastOrange, _lastGreen, _lastBuzzer;
    private bool _initialized;

    /// <summary>이전 상태와 달라진 경우에만 Coil 출력</summary>
    private async Task ApplyAsync(bool red, bool orange, bool green, bool buzzer, CancellationToken ct)
    {
        if (_initialized && red == _lastRed && orange == _lastOrange && green == _lastGreen && buzzer == _lastBuzzer)
            return;

        await _ioModuleService.SetTowerLampRedAsync(red, ct);
        await _ioModuleService.SetTowerLampYellowAsync(orange, ct);
        await _ioModuleService.SetTowerLampGreenAsync(green, ct);
        await _ioModuleService.SetTowerLampBuzzerAsync(buzzer, ct);

        _lastRed = red;
        _lastOrange = orange;
        _lastGreen = green;
        _lastBuzzer = buzzer;
        _initialized = true;

        _logger.LogDebug("SignalTower: Red={Red}, Orange={Orange}, Green={Green}, Buzzer={Buzzer}",
            red, orange, green, buzzer);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // 종료 시 모든 램프 OFF
        try
        {
            if (_ioModuleService.IsConnected)
            {
                await _ioModuleService.AllTowerLampsOffAsync(cancellationToken);
                await _ioModuleService.SetTowerLampBuzzerAsync(false, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalTower 종료 시 램프 OFF 실패");
        }

        _logger.LogInformation("SignalTowerService 종료");
        await base.StopAsync(cancellationToken);
    }
}
