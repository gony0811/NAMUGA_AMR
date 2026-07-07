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
    private readonly ILogger<IdleChargeService> _logger;

    /// <summary>자동 충전 기능 활성화</summary>
    public bool Enabled { get; set; } = true;

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
    private const int PollIntervalMs = 5000;

    public IdleChargeService(MoveSequenceRunner runner, CobotService cobotService, ILogger<IdleChargeService> logger)
    {
        _runner = runner;
        _cobotService = cobotService;
        _logger = logger;
    }

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

        // 이미 충전 노드에 있으면 트리거 안 함 (반복 방지)
        if (string.Equals(state.CurrentNodeId, ChargeNodeId, StringComparison.OrdinalIgnoreCase)) return;

        if (IdleSeconds < IdleTimeoutSeconds) return;

        // ★ Cobot 이 Manual(또는 미연결)이면 자동 충전 트리거 안 함 — 공통 게이트 사용
        if (await _cobotService.IsManualOrUnavailableAsync(stoppingToken))
        {
            _logger.LogInformation("Cobot Manual/미연결 — 자동 충전 트리거 보류");
            return;
        }

        // 트리거
        TriggerChargeSequence(stoppingToken);
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

        LastTriggerAt = DateTime.Now;
        LastActivityAt = DateTime.Now;   // 즉시 재트리거 방지

        _logger.LogInformation(
            "자동 충전 트리거 — Idle {Sec:F0}s 경과 (>= {Timeout}s), ChargeNode={Node}, CmdId={CmdId}",
            IdleSeconds, IdleTimeoutSeconds, ChargeNodeId, command.CmdId);

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
