using AMR.Enums;
using AMR.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 상위(ACS) 명령이 없고 시퀀스가 끝난 후 일정 시간(IdleTimeoutSeconds) 동안
/// 새 명령이 안 들어오면 자동으로 CHARGE 시퀀스(자가 생성한 moveCmd)를 실행.
///
/// 런타임 옵션:
///   - Enabled            : 기능 ON/OFF
///   - IdleTimeoutSeconds : Idle 판정 시간(초)
///   - ChargeNodeId       : 충전 목적지 NodeId
///
/// 웹 UI `/AutoCharge` 에서 토글/변경 가능.
/// </summary>
public class IdleChargeService : BackgroundService
{
    private readonly MoveSequenceRunner _runner;
    private readonly CobotService _cobotService;
    private readonly AmrService _amrService;
    private readonly IoModuleService _ioModuleService;
    private readonly SequenceSimulator _simulator;
    private readonly ILogger<IdleChargeService> _logger;

    /// <summary>자동 충전 기능 활성화</summary>
    public bool Enabled { get; set; } = false;   // v0.3.1: 기본 off — 복귀/충전 이동은 ACS 지시 원칙

    /// <summary>Idle 판정 시간(초) — 시퀀스 끝나고 이 시간 동안 명령 없으면 자동 충전</summary>
    public int IdleTimeoutSeconds { get; set; } = 20;

    /// <summary>충전 목적지 NodeId — 비어있으면 트리거 안 함</summary>
    public string ChargeNodeId { get; set; } = "N1001";

    /// <summary>마지막으로 활동(시퀀스 실행 / 마지막 완료)이 있었던 시점</summary>
    public DateTime LastActivityAt { get; private set; } = DateTime.Now;

    /// <summary>마지막 자동 충전 트리거 시점 (없으면 null)</summary>
    public DateTime? LastTriggerAt { get; private set; }

    /// <summary>현재 Idle 경과 시간(초)</summary>
    public double IdleSeconds => (DateTime.Now - LastActivityAt).TotalSeconds;

    private bool _previousIsRunning;
    private string? _holdReason;   // 자동 충전 보류 사유 — 사유가 바뀔 때만 로그 (40초마다 반복 출력 금지, R2)
    private const int PollIntervalMs = 5000;

    public IdleChargeService(MoveSequenceRunner runner, CobotService cobotService, AmrService amrService,
        IoModuleService ioModuleService, SequenceSimulator simulator, ILogger<IdleChargeService> logger)
    {
        _runner = runner;
        _cobotService = cobotService;
        _amrService = amrService;
        _ioModuleService = ioModuleService;
        _simulator = simulator;
        _logger = logger;
    }

    /// <summary>현재 자동 충전 보류 사유 (없으면 null) — 웹 UI 표시용</summary>
    public string? HoldReason => _holdReason;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IdleChargeService 시작 (Enabled={Enabled}, Timeout={Sec}s, ChargeNode={Node})",
            Enabled, IdleTimeoutSeconds, ChargeNodeId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IdleChargeService 평가 중 오류");
            }

            await Task.Delay(PollIntervalMs, stoppingToken);
        }
    }

    private async Task EvaluateOnceAsync(CancellationToken stoppingToken)
    {
        var state = _runner.State;

        // 시퀀스 진행 중 → 활동 시간 갱신, 트리거 안 함
        if (state.IsRunning)
        {
            LastActivityAt = DateTime.Now;
            _previousIsRunning = true;
            return;
        }

        // 방금 시퀀스가 끝난 시점 → Idle 타이머 리셋
        if (_previousIsRunning)
        {
            LastActivityAt = DateTime.Now;
            _previousIsRunning = false;
            _logger.LogDebug("시퀀스 종료 감지 — Idle 타이머 리셋");
            return;
        }

        // 데모 모드는 별개 — 자동 충전 트리거 안 함
        if (state.IsDemoRunning) return;

        // 기능 OFF 또는 ChargeNodeId 미설정 → 트리거 안 함
        if (!Enabled || string.IsNullOrWhiteSpace(ChargeNodeId)) return;

        // R2 (2026-09-15) 보류 조건 — 해당하는 동안 Idle 타이머를 계속 리셋해 조건 해소 후 IdleTimeout 을 다시 센다.
        //  - 잡 진행 중: 설비 앞 actionCmd/다음 명령 대기(ExchangeDocked)  (시퀀스 실행 중은 위에서 이미 처리)
        //  - 슬롯 점유: AMR 슬롯 1~4 중 하나라도 매거진 (매거진 실은 채 충전소로 가는 사고 방지)
        //  - 리셋 복구 중: 복구 마지막 AMR TASK 50 이 충전 이동을 덮어쓰는 것 방지 (2026-09-12)
        var hold = state.IsExchangeDocked ? "잡 진행 중"
                 : AnyAmrSlotOccupied() ? "슬롯 점유"
                 : _ioModuleService.IsRecoveryRunning ? "리셋 복구 중"
                 : null;
        if (hold != null)
        {
            LastActivityAt = DateTime.Now;
            if (_holdReason != hold)
            {
                _logger.LogInformation("자동 충전 보류: {Reason}", hold);
                _holdReason = hold;
            }
            return;
        }
        if (_holdReason != null)
        {
            _logger.LogInformation("자동 충전 보류 해제 ({Reason}) — Idle {Timeout}s 재계수", _holdReason, IdleTimeoutSeconds);
            _holdReason = null;
        }

        if (IdleSeconds < IdleTimeoutSeconds) return;

        // 이미 충전 노드에 있으면 트리거 안 함 (반복 방지) — 단, 실제로 충전 중일 때만.
        // 충전 노드로 기록돼 있는데 Discharging 이면 도착 오판·도킹 실패 등이므로 재트리거한다 (2026-09-12: 7분 방치 원인).
        var atChargeNode = string.Equals(state.CurrentNodeId, ChargeNodeId, StringComparison.OrdinalIgnoreCase);
        if (atChargeNode)
        {
            if (_amrService.LastStatus?.Battery.ChargingState == ChargingState.Charging) return;
            _logger.LogWarning("충전 노드({Node}) 기록이나 충전 중 아님(ChargingState={State}) — 자동 충전 재트리거",
                ChargeNodeId, _amrService.LastStatus?.Battery.ChargingState);
        }

        // ★ Cobot 이 Manual(또는 미연결)이면 자동 충전 트리거 안 함 — 공통 게이트 사용 (시뮬레이션 모드는 하드웨어 검증 생략)
        if (!_simulator.Enabled && await _cobotService.IsManualOrUnavailableAsync(stoppingToken))
        {
            _logger.LogInformation("Cobot Manual/미연결 — 자동 충전 트리거 보류");
            return;
        }

        // 트리거
        TriggerChargeSequence(stoppingToken);
    }

    /// <summary>AMR 슬롯 1~4 중 매거진 점유 여부 — 시뮬레이션이면 가상 슬롯, 아니면 I/O 모듈 MzDetect 센서</summary>
    private bool AnyAmrSlotOccupied()
    {
        if (_simulator.Enabled) return _simulator.AmrSlots.Any(o => o);
        return _ioModuleService.CurrentInputs is { } inp && (inp.MzDetect1 || inp.MzDetect2 || inp.MzDetect3 || inp.MzDetect4);
    }

    private void TriggerChargeSequence(CancellationToken stoppingToken)
    {
        var command = new AmrCommand
        {
            CmdId = $"auto_charge_{DateTime.Now:yyyyMMdd_HHmmss_fff}",
            Command = "moveCmd",
            NodeId = ChargeNodeId,
            JobType = "CHARGE",
            PortType = null,
            Port = null,
            AmrSlot = 1
        };

        var idleSeconds = IdleSeconds;
        LastTriggerAt = DateTime.Now;
        LastActivityAt = DateTime.Now;   // 즉시 재트리거 방지

        _logger.LogInformation(
            "자동 충전 트리거 — Idle {Sec:F0}s 경과 (>= {Timeout}s), ChargeNode={Node}, CmdId={CmdId}",
            idleSeconds, IdleTimeoutSeconds, ChargeNodeId, command.CmdId);

        // fire-and-forget — 시퀀스가 자체적으로 _runLock 으로 보호됨
        _ = Task.Run(async () =>
        {
            try
            {
                await _runner.RunSequenceAsync(command, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "자동 충전 시퀀스 실패");
            }
        }, stoppingToken);
    }
}
