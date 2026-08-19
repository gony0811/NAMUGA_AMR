using AMR.Data;
using AMR.Enums;
using AMR.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 메인 시퀀스 서비스 — AMR 상태 읽기/퍼블리시 + ACS 명령 처리
/// </summary>
public class MainSequenceService : BackgroundService
{
    private readonly AmrService _amrService;
    private readonly MqttService _mqttService;
    private readonly MoveSequenceRunner _sequenceRunner;
    private readonly AlarmService _alarmService;
    private readonly IoModuleService _ioModuleService;
    private readonly CobotService _cobotService;
    private readonly IdleChargeService _idleChargeService;
    private readonly SequenceSimulator _simulator;
    private readonly IDbContextFactory<AmrDbContext> _dbFactory;
    private readonly ILogger<MainSequenceService> _logger;

    public MainSequenceService(
        AmrService amrService,
        MqttService mqttService,
        MoveSequenceRunner sequenceRunner,
        AlarmService alarmService,
        IoModuleService ioModuleService,
        CobotService cobotService,
        IdleChargeService idleChargeService,
        SequenceSimulator simulator,
        IDbContextFactory<AmrDbContext> dbFactory,
        ILogger<MainSequenceService> logger)
    {
        _amrService = amrService;
        _mqttService = mqttService;
        _sequenceRunner = sequenceRunner;
        _alarmService = alarmService;
        _ioModuleService = ioModuleService;
        _cobotService = cobotService;
        _idleChargeService = idleChargeService;
        _simulator = simulator;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MainSequenceService 시작");

        // MQTT 명령 수신 이벤트 구독
        _mqttService.OnCommandReceived += command =>
            _ = HandleCommandAsync(command, stoppingToken);

        AmrStatusMessage? previousStatus = null;

        // 충전량·pose 주기 로그 (메인 루프는 1초마다 돌지만 30초 간격으로만 기록)
        var lastStatusLog = DateTime.MinValue;
        var statusLogInterval = TimeSpan.FromSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // AMR + MQTT 모두 연결된 경우에만 상태 퍼블리시.
                // 시뮬레이션 모드에서는 실 AMR 대신 가상 상태(pose/RunState/WorkState)를 발행 —
                // ACS 가 좌표+ARRIVED 로 도착 판정할 수 있도록.
                var simMode = _simulator.Enabled;
                if ((simMode || _amrService.IsConnected) && _mqttService.IsConnected)
                {
                    var robotStatus = simMode
                        ? _simulator.BuildRobotStatus()
                        : await _amrService.ReadStatusAsync(stoppingToken);

                    // 충전량·pose 주기 로그 (30초 간격)
                    var now = DateTime.UtcNow;
                    if (now - lastStatusLog >= statusLogInterval)
                    {
                        var battery = robotStatus.Battery;
                        var pose = robotStatus.Pose;
                        _logger.LogInformation(
                            "AMR 상태 — 충전량 {Level:F1}% (전압 {Voltage:F1}V, 전류 {Current:F2}A, {Charging}), " +
                            "Pose X={X:F2} Y={Y:F2} Angle={Angle:F1}°",
                            battery.LevelPercent, battery.Voltage, battery.Current, battery.ChargingState,
                            pose.X, pose.Y, pose.Angle);
                        lastStatusLog = now;
                    }

                    var alarm = simMode ? null : await _alarmService.EvaluateAsync(stoppingToken);

                    if (alarm != null && _sequenceRunner.State.IsRunning)
                    {
                        _logger.LogWarning("알람 감지 — 시퀀스 즉시 중단: {AlarmId} {AlarmName}", alarm.Id, alarm.Name);
                        _sequenceRunner.AbortWithAlarm(alarm);
                    }

                    var abnormal = _ioModuleService.CurrentAbnormal;
                    var statusMessage = AmrStatusMessage.FromRobotStatus(robotStatus, alarm, abnormal);

                    if (!statusMessage.Equals(previousStatus))
                    {
                        await _mqttService.PublishStatusAsync(statusMessage, stoppingToken);
                        previousStatus = statusMessage;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "메인 루프 실패");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    /// <summary>
    /// ACS 명령 처리 — moveCmd 수신 시 위치 태그 매핑을 조회하여 Task/Job 실행
    /// </summary>
    private async Task HandleCommandAsync(AmrCommand command, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("명령 처리 시작: {Command} NodeId={NodeId} CmdId={CmdId}",
                command.Command, command.NodeId, command.CmdId);

            if (command.Command == "moveCmd")
            {
                await HandleMoveCmdAsync(command, ct);
            }
            else if (command.Command == "exchangeCmd")
            {
                await HandleExchangeCmdAsync(command, ct);
            }
            else if (command.Command == "cancelCmd")
            {
                await HandleCancelCmdAsync(command, ct);
            }
            else if (command.Command == "actionCmd")
            {
                // actionCmd 는 MqttService 큐로 소비 (moveCmd Step5 / exchange 게이트) — 여기서는 무시
            }
            else
            {
                _logger.LogWarning("지원하지 않는 명령: {Command}", command.Command);
                await ReplyAsync(command.CmdId, "REJECTED", 2,
                    $"지원하지 않는 명령입니다: {command.Command}", ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "명령 처리 실패: CmdId={CmdId}", command.CmdId);
            await ReplyAsync(command.CmdId, "FAILED", 99,
                $"명령 처리 중 오류 발생: {ex.Message}", ct);
        }
    }

    private async Task HandleMoveCmdAsync(AmrCommand command, CancellationToken ct)
    {
        // 시뮬레이션 모드: 하드웨어(AMR/Cobot) 연결·상태 검증 생략 — ACS 통신 시뮬레이션 지원
        var sim = _simulator.Enabled;
        if (sim)
            _logger.LogInformation("[SIM] moveCmd 수락 검증 — 하드웨어 검증 생략 (NodeId={NodeId})", command.NodeId);

        // 1. AMR 연결 상태 확인
        if (!sim && !_amrService.IsConnected)
        {
            _logger.LogWarning("AMR 미연결 상태에서 moveCmd 수신: NodeId={NodeId}", command.NodeId);
            await ReplyAsync(command.CmdId, "REJECTED", 10,
                "AMR Modbus TCP가 연결되어 있지 않습니다.", ct);
            return;
        }

        // 2. 시퀀스 실행 중 확인
        if (_sequenceRunner.State.IsRunning)
        {
            _logger.LogWarning("시퀀스 실행 중에 moveCmd 수신: NodeId={NodeId}", command.NodeId);
            await ReplyAsync(command.CmdId, "REJECTED", 11,
                "시퀀스가 현재 실행 중입니다.", ct);
            return;
        }

        if (!sim)
        {
            // 3. 현재 작업 상태 확인 (Idle 상태에서만 이동 명령 수행)
            var status = await _amrService.ReadStatusAsync(ct);
            if (status.RobotState != RobotState.Stopped)
            {
                _logger.LogWarning("AMR이 작업 중(RobotState={RobotState})에 moveCmd 수신: NodeId={NodeId}",
                    status.RobotState, command.NodeId);
                await ReplyAsync(command.CmdId, "REJECTED", 11,
                    $"AMR이 현재 이동 중입니다. (상태: {status.RobotState})", ct);
                return;
            }

            // 4. Cobot Manual 모드(또는 미연결) 확인 — 작업자가 수동 조작 중이면 모든 이동 명령 거부
            if (await _cobotService.IsManualOrUnavailableAsync(ct))
            {
                _logger.LogWarning("Cobot Manual/미연결 상태에서 moveCmd 거부: NodeId={NodeId}", command.NodeId);
                await ReplyAsync(command.CmdId, "REJECTED", 12,
                    "Cobot이 Manual 모드이거나 연결되지 않아 이동 명령을 수행할 수 없습니다.", ct);
                return;
            }
        }

        // 5. 시퀀스 실행 (Fire-and-forget: 시퀀스는 백그라운드에서 진행)
        _logger.LogInformation("Move 시퀀스 시작: NodeId={NodeId}, Port={Port}, JobType={JobType}",
            command.NodeId, command.Port, command.JobType);
        _ = _sequenceRunner.RunSequenceAsync(command, ct);
    }

    /// <summary>
    /// exchangeCmd 수락 검증 및 시퀀스 시작 (docs/ACS-AMR_mqtt_exchangecmd.md 4장).
    /// 수락 조건: AMR 연결 · Idle · Cobot Auto/Run · 3개 노드 매핑 존재 · loadSlot/unloadSlot 빈 슬롯.
    /// </summary>
    private async Task HandleExchangeCmdAsync(AmrCommand command, CancellationToken ct)
    {
        var jobId = command.JobId;

        // 0. 필수 필드 검증
        if (string.IsNullOrWhiteSpace(jobId) ||
            string.IsNullOrWhiteSpace(command.LoadSourceNode) ||
            string.IsNullOrWhiteSpace(command.EquipNode) ||
            string.IsNullOrWhiteSpace(command.UnloadDestNode))
        {
            await ReplyAsync(command.CmdId, "REJECTED", 2,
                "exchangeCmd 필수 필드 누락 (jobId/loadSourceNode/equipNode/unloadDestNode)", ct, jobId);
            return;
        }

        // 시뮬레이션 모드: 하드웨어(AMR/Cobot/I/O) 검증 생략, 슬롯은 가상 상태 사용 — ACS 통신 시뮬레이션 지원
        var sim = _simulator.Enabled;
        if (sim)
            _logger.LogInformation("[SIM] exchangeCmd 수락 검증 — 하드웨어 검증 생략, 가상 슬롯 사용 (Job={JobId})", jobId);

        // 1. AMR 연결 상태 확인 → 10
        if (!sim && !_amrService.IsConnected)
        {
            _logger.LogWarning("AMR 미연결 상태에서 exchangeCmd 수신: Job={JobId}", jobId);
            await ReplyAsync(command.CmdId, "REJECTED", 10,
                "AMR Modbus TCP가 연결되어 있지 않습니다.", ct, jobId);
            return;
        }

        // 2. 시퀀스 실행 중 / AMR 이동 중 확인 → 11
        if (_sequenceRunner.State.IsRunning)
        {
            _logger.LogWarning("시퀀스 실행 중에 exchangeCmd 수신: Job={JobId}", jobId);
            await ReplyAsync(command.CmdId, "REJECTED", 11,
                "시퀀스가 현재 실행 중입니다.", ct, jobId);
            return;
        }

        if (!sim)
        {
            var status = await _amrService.ReadStatusAsync(ct);
            if (status.RobotState != RobotState.Stopped)
            {
                await ReplyAsync(command.CmdId, "REJECTED", 11,
                    $"AMR이 현재 이동 중입니다. (상태: {status.RobotState})", ct, jobId);
                return;
            }

            // 3. Cobot 준비 확인 → 22
            if (await _cobotService.IsManualOrUnavailableAsync(ct))
            {
                _logger.LogWarning("Cobot Manual/미연결 상태에서 exchangeCmd 거부: Job={JobId}", jobId);
                await ReplyAsync(command.CmdId, "REJECTED", 22,
                    "Cobot이 Manual 모드이거나 연결되지 않아 교환 명령을 수행할 수 없습니다.", ct, jobId);
                return;
            }
        }

        // 4. 슬롯 규칙(투입 1|2, 회수 3|4) 및 슬롯 상태 확인 → 21
        if (command.LoadSlot is not (1 or 2) || command.UnloadSlot is not (3 or 4))
        {
            await ReplyAsync(command.CmdId, "REJECTED", 21,
                $"슬롯 지정 오류 — loadSlot={command.LoadSlot}(허용 1|2), unloadSlot={command.UnloadSlot}(허용 3|4)", ct, jobId);
            return;
        }

        bool SlotOccupied(int slot)
        {
            if (sim) return _simulator.GetAmrSlot(slot);

            var inputs = _ioModuleService.CurrentInputs;
            if (inputs == null) return true;   // 미수신 시 점유로 간주 (아래에서 별도 거부)
            return slot switch
            {
                1 => inputs.MzDetect1,
                2 => inputs.MzDetect2,
                3 => inputs.MzDetect3,
                4 => inputs.MzDetect4,
                _ => true
            };
        }

        if (!sim && _ioModuleService.CurrentInputs == null)
        {
            await ReplyAsync(command.CmdId, "REJECTED", 21,
                "I/O 모듈 입력 미수신 — 슬롯 상태를 확인할 수 없습니다.", ct, jobId);
            return;
        }

        if (SlotOccupied(command.LoadSlot))
        {
            await ReplyAsync(command.CmdId, "REJECTED", 21,
                $"loadSlot {command.LoadSlot} 이 이미 점유 중입니다.", ct, jobId);
            return;
        }
        if (SlotOccupied(command.UnloadSlot))
        {
            await ReplyAsync(command.CmdId, "REJECTED", 21,
                $"unloadSlot {command.UnloadSlot} 이 이미 점유 중입니다.", ct, jobId);
            return;
        }

        // 5. 3개 노드 모두 위치 태그 매핑 존재 확인 → 20 (시뮬레이션은 이동을 생략하므로 매핑 불필요)
        if (!sim)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var nodes = new[] { command.LoadSourceNode!, command.EquipNode!, command.UnloadDestNode! };
            foreach (var node in nodes)
            {
                var exists = await db.LocationTagMappings.AnyAsync(m => m.LocationTag == node, ct);
                if (!exists)
                {
                    await ReplyAsync(command.CmdId, "REJECTED", 20,
                        $"위치 태그 매핑 없음: {node}", ct, jobId);
                    return;
                }
            }
        }

        // 6. 교환 시퀀스 실행 (Fire-and-forget) — ACCEPTED 는 러너가 발행
        _logger.LogInformation(
            "Exchange 시퀀스 시작: Job={JobId}, 픽업={Load}, 설비={Equip}({Port}), 반납={Dest}, 슬롯={LoadSlot}/{UnloadSlot}",
            jobId, command.LoadSourceNode, command.EquipNode, command.Port, command.UnloadDestNode,
            command.LoadSlot, command.UnloadSlot);
        _ = _sequenceRunner.RunExchangeSequenceAsync(command, ct);
    }

    /// <summary>
    /// cancelCmd 처리 (docs/ACS-AMR_mqtt_exchangecmd.md 7장).
    /// 실행 중 jobId 일치(C2/C3) → CANCELED(0) 응답 후 러너가 중단/복귀 수행.
    /// 미실행/불일치(C4) → CANCELED(40, CANCEL_REJECTED).
    /// </summary>
    private async Task HandleCancelCmdAsync(AmrCommand command, CancellationToken ct)
    {
        var jobId = command.JobId;

        if (string.IsNullOrWhiteSpace(jobId))
        {
            await ReplyAsync(command.CmdId, "CANCELED", 40,
                "CANCEL_REJECTED — jobId 누락", ct, jobId);
            return;
        }

        // 복귀 노드: cancelCmd.returnNode 지정 시 사용, 생략 시 자동충전 노드 (협의 #3 초안 가정)
        var returnNode = string.IsNullOrWhiteSpace(command.ReturnNode)
            ? _idleChargeService.ChargeNodeId
            : command.ReturnNode;

        var accepted = _sequenceRunner.RequestCancel(jobId!, returnNode);

        if (accepted)
        {
            _logger.LogWarning("Job 취소 승인: Job={JobId}, 복귀노드={ReturnNode}", jobId, returnNode);
            await ReplyAsync(command.CmdId, "CANCELED", 0,
                $"취소 승인: Job={jobId} (복귀노드={returnNode ?? "미지정"})", ct, jobId);
        }
        else
        {
            _logger.LogWarning("Job 취소 거부(C4): Job={JobId} — 미실행 또는 jobId 불일치", jobId);
            await ReplyAsync(command.CmdId, "CANCELED", 40,
                $"CANCEL_REJECTED — 해당 Job 이 실행 중이 아니거나 이미 종료되었습니다: {jobId}", ct, jobId);
        }
    }

    private async Task ReplyAsync(string cmdId, string status, int resultCode, string message,
        CancellationToken ct, string? jobId = null)
    {
        var reply = new CommandReply
        {
            CmdId = cmdId,
            JobId = jobId,
            Status = status,
            ResultCode = resultCode,
            Message = message,
            Timestamp = DateTime.UtcNow.ToString("o")
        };

        await _mqttService.PublishReplyAsync(reply, ct);
    }
}
