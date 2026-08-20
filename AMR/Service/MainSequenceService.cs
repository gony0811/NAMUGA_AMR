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

                    // 사양(mqtt_interface.md): status 는 1초 주기 발행 (Retain).
                    // 변경 시에만 발행하면 ACS 가 무변화 구간을 연결 끊김으로 판정하므로 매 주기 발행한다.
                    await _mqttService.PublishStatusAsync(statusMessage, stoppingToken);
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
            else if (command.Command == "actionCmd")
            {
                await HandleActionCmdAsync(command, ct);
            }
            else if (command.Command == "cancelCmd")
            {
                await HandleCancelCmdAsync(command, ct);
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
                "AMR Modbus TCP가 연결되어 있지 않습니다.", ct, command.JobId ?? command.CmdId);
            return;
        }

        // 2. 시퀀스 실행 중 확인
        if (_sequenceRunner.State.IsRunning)
        {
            _logger.LogWarning("시퀀스 실행 중에 moveCmd 수신: NodeId={NodeId}", command.NodeId);
            await ReplyAsync(command.CmdId, "REJECTED", 11,
                "시퀀스가 현재 실행 중입니다.", ct, command.JobId ?? command.CmdId);
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
                await ReplyAsync(command.CmdId, "REJECTED", 22,
                    "Cobot이 Manual 모드이거나 연결되지 않아 이동 명령을 수행할 수 없습니다.", ct);
                return;
            }
        }

        // 5. amrSlot 용도·상태 검증 (v0.3 §6 resultCode 21)
        //    슬롯 용도 고정: 1|2 = NEW 매거진 픽업(투입슬롯), 3|4 = OLD 매거진 회수·반납(회수슬롯)
        //    - UNLOAD(자재포트 픽업 → AMR): 슬롯 1|2 + 빈 슬롯이어야 함
        //    - LOAD(AMR → 자재포트 반납): 슬롯 3|4 + 매거진이 있어야 함
        //    EXCHANGE(설비행 도킹)·CHARGE 는 슬롯 조작 없음 → 검증 생략.
        var jt = (command.JobType ?? "").ToUpperInvariant();
        if (jt is "UNLOAD" or "LOAD")
        {
            var slot = command.AmrSlot;
            var slotRangeOk = jt == "UNLOAD" ? slot is 1 or 2 : slot is 3 or 4;
            if (!slotRangeOk)
            {
                await ReplyAsync(command.CmdId, "REJECTED", 21,
                    $"amrSlot {slot} 용도 위반 — {(jt == "UNLOAD" ? "픽업(UNLOAD)은 투입슬롯 1|2" : "반납(LOAD)은 회수슬롯 3|4")} 만 사용합니다.", ct,
                    command.JobId ?? command.CmdId);
                return;
            }

            bool? occupied = null;
            if (sim) occupied = _simulator.GetAmrSlot(slot);
            else if (_ioModuleService.CurrentInputs is { } inp)
                occupied = slot switch { 1 => inp.MzDetect1, 2 => inp.MzDetect2, 3 => inp.MzDetect3, _ => inp.MzDetect4 };

            if (occupied is bool occ)
            {
                var expectOccupied = jt == "LOAD";
                if (occ != expectOccupied)
                {
                    await ReplyAsync(command.CmdId, "REJECTED", 21,
                        $"amrSlot {slot} 상태 불일치 — {jt} 에는 {(expectOccupied ? "매거진이 있어야" : "빈 슬롯이어야")} 합니다.", ct,
                        command.JobId ?? command.CmdId);
                    return;
                }
            }
        }

        // 6. 시퀀스 실행 (Fire-and-forget: 시퀀스는 백그라운드에서 진행)
        _logger.LogInformation("Move 시퀀스 시작: NodeId={NodeId}, Port={Port}, JobType={JobType}, AmrSlot={Slot}",
            command.NodeId, command.Port, command.JobType, command.AmrSlot);
        _ = _sequenceRunner.RunSequenceAsync(command, ct);
    }

    /// <summary>
    /// actionCmd 처리 (v0.3 §4.2).
    ///   - 설비 앞 도킹 대기(ExchangeDocked) 상태: jobId 대조 후 RunActionAsync 로 독립 실행 (UNLOAD/LOAD 작업 + COMPLETED)
    ///   - 일반 moveCmd 설비포트 Step5 대기 중: MqttService 큐에 이미 들어가 있으므로 시퀀스가 소비 — 여기서는 무시
    ///   - 그 외(미도킹·미실행): 무시 + 경고 로그 (사양: "그 외는 무시+로그")
    /// </summary>
    private async Task HandleActionCmdAsync(AmrCommand command, CancellationToken ct)
    {
        var state = _sequenceRunner.State;

        if (state.IsExchangeDocked && !state.IsRunning)
        {
            var jobOk = string.IsNullOrWhiteSpace(command.JobId) ||
                        string.Equals(command.JobId, state.JobId, StringComparison.OrdinalIgnoreCase);
            if (!jobOk)
            {
                _logger.LogWarning("actionCmd jobId 불일치 — 무시 (수신={Recv}, 진행={Cur})", command.JobId, state.JobId);
                return;
            }

            var kind = (command.Type ?? command.JobType ?? "").ToUpperInvariant();
            if (kind is not ("UNLOAD" or "LOAD"))
            {
                _logger.LogWarning("actionCmd type 불명 — 무시 (type={Type}, jobType={JobType})", command.Type, command.JobType);
                return;
            }

            // 슬롯 용도 검증: UNLOAD(OLD 회수→AMR)=회수슬롯 3|4, LOAD(NEW 투입←AMR)=투입슬롯 1|2
            var actSlotOk = kind == "UNLOAD" ? command.AmrSlot is 3 or 4 : command.AmrSlot is 1 or 2;
            if (!actSlotOk)
            {
                await ReplyAsync(command.CmdId, "REJECTED", 21,
                    $"amrSlot {command.AmrSlot} 용도 위반 — actionCmd {(kind == "UNLOAD" ? "UNLOAD(회수)는 3|4" : "LOAD(투입)는 1|2")} 만 사용합니다.", ct,
                    command.JobId ?? state.JobId);
                return;
            }

            _logger.LogInformation("actionCmd 독립 실행: type={Type}, amrSlot={Slot}, port={Port} (Job={JobId})",
                kind, command.AmrSlot, command.Port, state.JobId);
            _ = _sequenceRunner.RunActionAsync(command, ct);
            return;
        }

        if (state.IsRunning)
        {
            // moveCmd 설비포트 Step5(WaitActionCmd) 가 MqttService 큐에서 소비
            _logger.LogInformation("actionCmd — 진행 중 시퀀스가 큐에서 소비 (CmdId={CmdId})", command.CmdId);
            return;
        }

        _logger.LogWarning("actionCmd 무시 — 설비 도킹 대기/진행 중 상태가 아님 (CmdId={CmdId})", command.CmdId);
    }

    /// <summary>
    /// cancelCmd 처리 (v0.3 §4.3) — 진행 중 명령 폐기 → 정지 → Idle → CANCELED(0).
    /// 미진행/jobId 불일치 → CANCELED(40, CANCEL_REJECTED). 복귀 이동·ALARM 은 ACS 가 별도 처리.
    /// </summary>
    private async Task HandleCancelCmdAsync(AmrCommand command, CancellationToken ct)
    {
        var jobId = command.JobId ?? command.CmdId;

        if (string.IsNullOrWhiteSpace(jobId))
        {
            await ReplyAsync(command.CmdId, "CANCELED", 40, "CANCEL_REJECTED — jobId 누락", ct, jobId);
            return;
        }

        var accepted = _sequenceRunner.RequestCancel(jobId);
        if (accepted)
        {
            _logger.LogWarning("Job 취소 승인: Job={JobId} — 정지 후 Idle", jobId);
            await ReplyAsync(command.CmdId, "CANCELED", 0, $"취소 승인: Job={jobId}", ct, jobId);
        }
        else
        {
            _logger.LogWarning("Job 취소 거부(C4): Job={JobId} — 미진행 또는 jobId 불일치", jobId);
            await ReplyAsync(command.CmdId, "CANCELED", 40,
                $"CANCEL_REJECTED — 해당 Job 이 진행 중이 아니거나 이미 종료되었습니다: {jobId}", ct, jobId);
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
