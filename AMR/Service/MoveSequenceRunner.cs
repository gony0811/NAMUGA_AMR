using System.Collections.Concurrent;
using AMR.Data;
using AMR.Enums;
using AMR.Models;
using AMR.Service.Camera;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// Move Command 시퀀스 실행기 — 10단계 상태머신
/// MainSequenceService에서 호출되며, 웹 UI에서 단계별 수동 실행도 지원.
/// </summary>
public class MoveSequenceRunner
{
    private readonly AmrService _amrService;
    private readonly CobotService _cobotService;
    private readonly MqttService _mqttService;
    private readonly CameraService _cameraService;
    private readonly MagazineDetectionService _magazineDetection;
    private readonly PortOffsetService _portOffset;
    private readonly ModelOffsetService _modelOffset;
    // IoModuleService 는 Func<T> 로 주입 — IoModuleService 가 MoveSequenceRunner 를 받기 때문에
    // 직접 주입하면 순환 의존성으로 DI 초기화 실패. Autofac 은 Func<T> 를 relational type 으로
    // 자동 지원해서 lazy resolve 가능 → 순환 깸. (Lazy<T> 는 Autofac 기본 미지원)
    private readonly Func<IoModuleService> _ioModuleServiceFactory;
    private IoModuleService _ioModuleService => _ioModuleServiceFactory();
    private readonly IDbContextFactory<AmrDbContext> _dbFactory;
    private readonly ILogger<MoveSequenceRunner> _logger;

    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly ConcurrentQueue<SequenceLogEntry> _logs = new();
    private CancellationTokenSource? _sequenceCts;
    private CancellationTokenSource? _demoCts;
    private Alarm? _abortAlarm;

    private const int MaxLogEntries = 200;
    private const int ArrivalTimeoutSeconds = 120;
    private const int CobotTimeoutSeconds = 60;
    private const int PollIntervalMs = 500;

    public MoveSequenceRunner(
        AmrService amrService,
        CobotService cobotService,
        MqttService mqttService,
        CameraService cameraService,
        MagazineDetectionService magazineDetection,
        PortOffsetService portOffset,
        ModelOffsetService modelOffset,
        Func<IoModuleService> ioModuleServiceFactory,
        IDbContextFactory<AmrDbContext> dbFactory,
        ILogger<MoveSequenceRunner> logger)
    {
        _amrService = amrService;
        _cobotService = cobotService;
        _mqttService = mqttService;
        _cameraService = cameraService;
        _magazineDetection = magazineDetection;
        _portOffset = portOffset;
        _modelOffset = modelOffset;
        _ioModuleServiceFactory = ioModuleServiceFactory;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>현재 시퀀스 상태</summary>
    public SequenceState State { get; } = new();

    /// <summary>최근 로그 조회</summary>
    public IReadOnlyList<SequenceLogEntry> GetRecentLogs(int count = 50)
    {
        return _logs.Reverse().Take(count).Reverse().ToList();
    }

    /// <summary>전체 시퀀스 실행 (moveCmd 수신 시 호출)</summary>
    public async Task RunSequenceAsync(AmrCommand command, CancellationToken ct)
    {
        if (!await _runLock.WaitAsync(0, ct))
        {
            _logger.LogWarning("시퀀스 이미 실행 중 — 요청 무시");
            return;
        }

        _sequenceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _sequenceCts.Token;

        try
        {
            State.IsRunning = true;
            State.ErrorMessage = null;
            State.StartedAt = DateTime.Now;

            // OPERATOR_ABORT abnormal 은 운전자가 reset 으로 코봇을 Auto 복귀시킬 때 해제되는 것이
            // 정상 경로(IoModuleService.HandleResetSwitchAsync). 다만 그 과정 없이 새 job 이 들어오면
            // 잔류 abnormal 때문에 새 job 도 즉시 삭제될 수 있으므로, 여기서 방어적으로 한 번 더 해제.
            if (_ioModuleService.CurrentAbnormal?.Type == "OPERATOR_ABORT")
            {
                _logger.LogInformation("새 job 시작 — 잔류 OPERATOR_ABORT abnormal 해제 (fallback)");
                _ioModuleService.ClearAbnormal();
            }

            // Step 1: MoveCmdReceived
            await ExecuteStepInternalAsync(SequenceStep.MoveCmdReceived, command, token);

            // Step 2: MoveCmdReply
            await ExecuteStepInternalAsync(SequenceStep.MoveCmdReply, command, token);

            // JobType 에 "CHARGE" 문자열이 포함되면 충전 시퀀스 (예: "CHARGE", "GO_CHARGE", "CHARGE_FAST" 등)
            var isCharge = command.JobType?.Contains("CHARGE", StringComparison.OrdinalIgnoreCase) ?? false;

            // CHARGE 작업: AMR 이동 전 Cobot 을 Phome 으로 (충돌 방지)
            if (isCharge)
            {
                AddLog(SequenceStep.SendMoveCommand, "CHARGE 작업 — AMR 이동 전 Cobot Phome 이동");
                await SendCobotCommandAndWaitAsync(25, "Phome (CHARGE 사전 이동)", token);
            }

            // 이동 시작 — 도착 전까지 현재 위치 정보 무효화 (무조건 이동, 스킵 없음)
            State.CurrentNodeId = null;

            // Step 3: SendMoveCommand
            await ExecuteStepInternalAsync(SequenceStep.SendMoveCommand, command, token);

            // Step 4: WaitArrival
            await ExecuteStepInternalAsync(SequenceStep.WaitArrival, command, token);

            // CHARGE 작업은 도착 즉시 완료 (Step 5~9 스킵, Cobot 은 이미 Phome 에 있음)
            if (isCharge)
            {
                await CompleteChargeAsync(command, token);
                return;
            }

            // Step 5: WaitActionCmd
            await ExecuteStepInternalAsync(SequenceStep.WaitActionCmd, command, token);

            // Step 6: CobotQrPosition
            await ExecuteStepInternalAsync(SequenceStep.CobotQrPosition, command, token);

            // Step 7: CameraQrRead
            await ExecuteStepInternalAsync(SequenceStep.CameraQrRead, command, token);

            // Step 8: CobotPickup
            await ExecuteStepInternalAsync(SequenceStep.CobotPickup, command, token);

            // Step 9: CobotPlace
            await ExecuteStepInternalAsync(SequenceStep.CobotPlace, command, token);

            // Step 10: Complete
            await ExecuteStepInternalAsync(SequenceStep.Complete, command, token);
        }
        catch (OperationCanceledException)
        {
            if (_abortAlarm is { } alarm)
            {
                State.CurrentStep = SequenceStep.Faulted;
                State.ErrorMessage = $"[{alarm.Id}] {alarm.Name}";
                AddLog(SequenceStep.Faulted, $"알람으로 시퀀스 중단: [{alarm.Id}] {alarm.Name}", true);
                _logger.LogWarning("알람으로 시퀀스 중단: {AlarmId} {AlarmName}", alarm.Id, alarm.Name);
            }
            else
            {
                AddLog(State.CurrentStep, "시퀀스 취소됨", true);
                State.CurrentStep = SequenceStep.Idle;
                _logger.LogWarning("시퀀스 취소됨");
            }
        }
        catch (Exception ex)
        {
            State.CurrentStep = SequenceStep.Faulted;
            State.ErrorMessage = ex.Message;
            AddLog(SequenceStep.Faulted, $"시퀀스 실패: {ex.Message}", true);
            _logger.LogError(ex, "시퀀스 실행 실패");
        }
        finally
        {
            State.IsRunning = false;
            _abortAlarm = null;
            _sequenceCts?.Dispose();
            _sequenceCts = null;
            _runLock.Release();
        }
    }

    /// <summary>단일 Step 수동 실행 (웹 UI 테스트용)</summary>
    public async Task ExecuteStepAsync(SequenceStep step, AmrCommand? command, CancellationToken ct)
    {
        if (!await _runLock.WaitAsync(0, ct))
        {
            AddLog(step, "시퀀스 실행 중이므로 수동 Step 실행 불가", true);
            return;
        }

        try
        {
            // 수동 실행 시 command가 없으면 현재 State의 정보 사용
            var cmd = command ?? new AmrCommand
            {
                CmdId = State.CmdId ?? $"manual_{DateTime.Now:HHmmss}",
                Command = "moveCmd",
                NodeId = State.NodeId ?? "N0001",
                Port = State.Port,
                JobType = State.JobType,
                PortType = State.PortType,
                AmrSlot = State.AmrSlot
            };

            await ExecuteStepInternalAsync(step, cmd, ct);
        }
        catch (Exception ex)
        {
            State.CurrentStep = SequenceStep.Faulted;
            State.ErrorMessage = ex.Message;
            AddLog(SequenceStep.Faulted, $"Step 실행 실패: {ex.Message}", true);
            _logger.LogError(ex, "수동 Step {Step} 실행 실패", step);
        }
        finally
        {
            _runLock.Release();
        }
    }

    /// <summary>실행 중인 시퀀스 중단</summary>
    public void AbortSequence()
    {
        if (_sequenceCts is { IsCancellationRequested: false })
        {
            _sequenceCts.Cancel();
            AddLog(State.CurrentStep, "시퀀스 중단 요청", true);
            _logger.LogWarning("시퀀스 중단 요청");
        }
    }

    /// <summary>Faulted 상태 해제 — 리셋 스위치 등 외부 복구 시 호출</summary>
    public void ClearFault()
    {
        if (State.CurrentStep == SequenceStep.Faulted)
        {
            AddLog(SequenceStep.Faulted, "Fault 상태 해제 → Idle 복귀", false);
            State.CurrentStep = SequenceStep.Idle;
            State.ErrorMessage = null;
            _logger.LogInformation("Fault 상태 해제 — Idle 복귀");
        }
    }

    /// <summary>알람 발생으로 인한 시퀀스 즉시 중단 — Faulted 상태로 전환</summary>
    public void AbortWithAlarm(Alarm alarm)
    {
        _abortAlarm = alarm;
        AbortSequence();
    }

    /// <summary>데모 모드 실행 — N001/N006에서 LOAD↔UNLOAD 반복</summary>
    public async Task RunDemoAsync(CancellationToken ct)
    {
        if (State.IsDemoRunning || State.IsRunning)
        {
            _logger.LogWarning("데모 시작 불가 — 이미 실행 중");
            return;
        }

        _demoCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var demoToken = _demoCts.Token;

        State.IsDemoRunning = true;
        State.DemoCycle = 0;
        State.DemoStepIndex = 0;
        AddLog(SequenceStep.Idle, "===== 데모 모드 시작 =====");

        var demoSequences = new[]
        {
            new { NodeId = "N001", JobType = "LOAD",   PortType = "MATERIAL", Port = "LEFT", AmrSlot = 1 },
            new { NodeId = "N001", JobType = "UNLOAD", PortType = "MATERIAL", Port = "LEFT", AmrSlot = 1 },
            new { NodeId = "N006", JobType = "LOAD",   PortType = "MATERIAL", Port = "LEFT", AmrSlot = 1 },
            new { NodeId = "N006", JobType = "UNLOAD", PortType = "MATERIAL", Port = "LEFT", AmrSlot = 1 },
        };

        try
        {
            while (!demoToken.IsCancellationRequested)
            {
                State.DemoCycle++;

                for (int i = 0; i < demoSequences.Length; i++)
                {
                    demoToken.ThrowIfCancellationRequested();

                    State.DemoStepIndex = i;
                    var seq = demoSequences[i];

                    var command = new AmrCommand
                    {
                        CmdId = $"demo_{State.DemoCycle}_{i + 1}_{DateTime.Now:HHmmss_fff}",
                        Command = "moveCmd",
                        NodeId = seq.NodeId,
                        JobType = seq.JobType,
                        PortType = seq.PortType,
                        Port = seq.Port,
                        AmrSlot = seq.AmrSlot
                    };

                    AddLog(SequenceStep.Idle,
                        $"[데모 Cycle {State.DemoCycle} - {i + 1}/4] {seq.NodeId} {seq.JobType} 시작");

                    await RunSequenceAsync(command, demoToken);

                    // 시퀀스 실패 시 데모 중단
                    if (State.CurrentStep == SequenceStep.Faulted)
                    {
                        AddLog(SequenceStep.Faulted, "데모 모드 중단 — 시퀀스 실패", true);
                        return;
                    }

                    // 다음 시퀀스 전 짧은 대기
                    if (!demoToken.IsCancellationRequested)
                        await Task.Delay(2000, demoToken);
                }

                AddLog(SequenceStep.Idle, $"데모 Cycle {State.DemoCycle} 완료 — 다음 사이클 시작");
            }
        }
        catch (OperationCanceledException)
        {
            AddLog(SequenceStep.Idle, "데모 모드 정지 요청 — 중지됨");
            _logger.LogInformation("데모 모드 정지");
        }
        finally
        {
            State.IsDemoRunning = false;
            _demoCts?.Dispose();
            _demoCts = null;
            AddLog(SequenceStep.Idle, "===== 데모 모드 종료 =====");
        }
    }

    /// <summary>데모 모드 정지 — 현재 시퀀스 완료 후 중단</summary>
    public void StopDemo()
    {
        if (_demoCts is { IsCancellationRequested: false })
        {
            _demoCts.Cancel();
            AddLog(SequenceStep.Idle, "데모 모드 정지 요청");
            _logger.LogWarning("데모 모드 정지 요청");
        }
    }

    #region Step 구현

    private async Task ExecuteStepInternalAsync(SequenceStep step, AmrCommand command, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        State.CurrentStep = step;
        State.StepStartedAt = DateTime.Now;

        switch (step)
        {
            case SequenceStep.MoveCmdReceived:
                await Step_MoveCmdReceived(command);
                break;
            case SequenceStep.MoveCmdReply:
                await Step_MoveCmdReply(command, ct);
                break;
            case SequenceStep.SendMoveCommand:
                await Step_SendMoveCommand(command, ct);
                break;
            case SequenceStep.WaitArrival:
                await Step_WaitArrival(command, ct);
                break;
            case SequenceStep.WaitActionCmd:
                await Step_WaitActionCmd(command, ct);
                break;
            case SequenceStep.CobotQrPosition:
                await Step_CobotQrPosition(command, ct);
                break;
            case SequenceStep.CameraQrRead:
                await Step_CameraQrRead(command, ct);
                break;
            case SequenceStep.CobotPickup:
                await Step_CobotPickup(command, ct);
                break;
            case SequenceStep.CobotPlace:
                await Step_CobotPlace(command, ct);
                break;
            case SequenceStep.Complete:
                await Step_Complete(command, ct);
                break;
        }
    }

    /// <summary>Step 1: moveCmd 수신 — command 정보 저장</summary>
    private Task Step_MoveCmdReceived(AmrCommand command)
    {
        State.CmdId = command.CmdId;
        State.NodeId = command.NodeId;
        State.Port = command.Port;
        State.JobType = command.JobType;
        State.PortType = command.PortType;
        State.AmrSlot = command.AmrSlot;

        AddLog(SequenceStep.MoveCmdReceived,
            $"MoveCmd 수신: NodeId={command.NodeId}, Port={command.Port ?? "없음"}, JobType={command.JobType ?? "없음"}, PortType={command.PortType ?? "없음"}, AmrSlot={command.AmrSlot}");

        return Task.CompletedTask;
    }

    /// <summary>Step 2: ACS에 ACCEPTED 응답 전송</summary>
    private async Task Step_MoveCmdReply(AmrCommand command, CancellationToken ct)
    {
        var reply = new CommandReply
        {
            CmdId = command.CmdId,
            Status = "ACCEPTED",
            ResultCode = 0,
            Message = $"이동 명령 수락: {command.NodeId}",
            Timestamp = DateTime.UtcNow.ToString("o")
        };

        await _mqttService.PublishReplyAsync(reply, ct);
        AddLog(SequenceStep.MoveCmdReply, $"MoveCmdReply 전송: ACCEPTED (CmdId={command.CmdId})");
    }

    /// <summary>Step 3: NodeId → TaskIndex/JobIndex 변환 후 AMR 이동 명령</summary>
    private async Task Step_SendMoveCommand(AmrCommand command, CancellationToken ct)
    {
        // DB에서 위치 태그 매핑 조회
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var mapping = await db.LocationTagMappings
            .FirstOrDefaultAsync(m => m.LocationTag == command.NodeId, ct);

        if (mapping == null)
            throw new InvalidOperationException($"위치 태그 매핑 없음: {command.NodeId}");

        // AMR에 TaskIndex, JobIndex 설정 후 시작
        await _amrService.SetTaskIndexAsync((ushort)mapping.TaskIndex, ct);
        await _amrService.SetJobIndexAsync((ushort)mapping.JobIndex, ct);
        await _amrService.SetExecutionControlAsync(ExecutionControl.Start, ct);

        AddLog(SequenceStep.SendMoveCommand,
            $"AMR 이동 명령: NodeId={command.NodeId} → Task={mapping.TaskIndex}, Job={mapping.JobIndex}");
    }

    /// <summary>Step 4: AMR 도착 대기 — RobotState가 Started→Stopped 전이를 확인하여 도착 판단</summary>
    private async Task Step_WaitArrival(AmrCommand command, CancellationToken ct)
    {
        AddLog(SequenceStep.WaitArrival, "AMR 도착 대기 시작");

        var deadline = DateTime.Now.AddSeconds(ArrivalTimeoutSeconds);

        // Phase 1: RobotState 가 Started 가 될 때까지 대기 (이동 시작 확인)
        while (!ct.IsCancellationRequested)
        {
            if (DateTime.Now > deadline)
                throw new TimeoutException($"AMR 이동 시작 대기 타임아웃 ({ArrivalTimeoutSeconds}초)");

            var status = await _amrService.ReadStatusAsync(ct);

            if (status.RobotState == RobotState.Started)
            {
                AddLog(SequenceStep.WaitArrival, "AMR 이동 시작 확인 (RobotState=Started)");
                break;
            }

            await Task.Delay(PollIntervalMs, ct);
        }

        ct.ThrowIfCancellationRequested();

        // Phase 2: RobotState 가 Stopped 가 될 때까지 대기 (이동 완료 확인)
        while (!ct.IsCancellationRequested)
        {
            var status = await _amrService.ReadStatusAsync(ct);

            if (status.RobotState == RobotState.Stopped)
            {
                await RecordArrivalAsync(command, ct);
                return;
            }

            await Task.Delay(PollIntervalMs, ct);
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>도착 시점에 CurrentNodeId 와 pose 정보를 로그에 기록</summary>
    private async Task RecordArrivalAsync(AmrCommand command, CancellationToken ct)
    {
        State.CurrentNodeId = command.NodeId;

        try
        {
            var arrivedPose = await _amrService.ReadPoseAsync(ct);
            AddLog(SequenceStep.WaitArrival,
                $"AMR 도착 완료 (NodeId={command.NodeId}, Pose=({arrivedPose.X:F1}, {arrivedPose.Y:F1}, {arrivedPose.Angle:F1}°))");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "도착 pose 읽기 실패");
            AddLog(SequenceStep.WaitArrival,
                $"AMR 도착 완료 (NodeId={command.NodeId}, Pose 읽기 실패)");
        }
    }

    /// <summary>Step 5: ARRIVED 전송 → ActionCmd 대기 (설비포트만 대기, 자재포트는 스킵)</summary>
    private async Task Step_WaitActionCmd(AmrCommand command, CancellationToken ct)
    {
        // 포트 타입에 관계없이 ARRIVED reply 전송
        var arrivedReply = new CommandReply
        {
            CmdId = command.CmdId,
            Status = "ARRIVED",
            ResultCode = 0,
            Message = $"AMR 도착: {command.NodeId}",
            Timestamp = DateTime.UtcNow.ToString("o")
        };
        await _mqttService.PublishReplyAsync(arrivedReply, ct);
        AddLog(SequenceStep.WaitActionCmd, $"ARRIVED 전송 완료 (CmdId={command.CmdId})");

        // PortType 에 "EQP" 가 포함되어 있으면 설비포트, 아니면 자재포트
        var isFacility = command.PortType?.Contains("EQP", StringComparison.OrdinalIgnoreCase) ?? false;

        if (!isFacility)
        {
            AddLog(SequenceStep.WaitActionCmd,
                $"자재포트 — ActionCmd 대기 없이 다음 단계 진행 (PortType={command.PortType ?? "없음"})");
            return;
        }

        // 설비포트: ActionCmd 수신 대기
        AddLog(SequenceStep.WaitActionCmd, "설비포트 — ActionCmd 수신 대기 시작");

        var deadline = DateTime.Now.AddSeconds(ArrivalTimeoutSeconds);

        while (!ct.IsCancellationRequested)
        {
            if (DateTime.Now > deadline)
                throw new TimeoutException($"ActionCmd 수신 타임아웃 ({ArrivalTimeoutSeconds}초)");

            if (_mqttService.TryDequeueActionCmd(out var actionCmd))
            {
                AddLog(SequenceStep.WaitActionCmd,
                    $"ActionCmd 수신 완료 (CmdId={actionCmd.CmdId}, Port={actionCmd.Port ?? "없음"})");

                // ★ ActionCmd.Port 가 있으면 MoveCmd.Port 를 덮어씀
                //   설계 의도: MoveCmd 는 "어디로 갈지", ActionCmd 가 "어느 슬롯에 작업할지" 최종 결정.
                //   AmrSlot / JobType / PortType 은 그대로 MoveCmd 값 유지 (사용자 명시 — Port 만 덮어씀)
                if (!string.IsNullOrWhiteSpace(actionCmd.Port))
                {
                    var oldPort = command.Port ?? "없음";
                    command.Port = actionCmd.Port;
                    AddLog(SequenceStep.WaitActionCmd,
                        $"Port 갱신: MoveCmd({oldPort}) → ActionCmd({actionCmd.Port})");
                }
                else
                {
                    AddLog(SequenceStep.WaitActionCmd,
                        $"ActionCmd 에 Port 없음 — MoveCmd.Port({command.Port ?? "없음"}) 그대로 사용");
                }

                return;
            }

            await Task.Delay(PollIntervalMs, ct);
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Step 6: Cobot을 QR 코드 읽기 위치로 이동</summary>
    private async Task Step_CobotQrPosition(AmrCommand command, CancellationToken ct)
    {
        // PortType 에 "EQP" 가 포함되어 있으면 설비포트, 아니면 자재포트
        var isFacility = command.PortType?.Contains("EQP", StringComparison.OrdinalIgnoreCase) ?? false;
        var portKind = isFacility ? "설비포트" : "자재포트";
        ushort qrDiIndex = isFacility ? (ushort)16 : (ushort)17;

        AddLog(SequenceStep.CobotQrPosition, $"Cobot QR 읽기 위치 이동 (DI{qrDiIndex}, {portKind})");
        await SendCobotCommandAndWaitAsync(qrDiIndex, $"QR 읽기 위치 이동 ({portKind})", ct);
        AddLog(SequenceStep.CobotQrPosition, "Cobot QR 읽기 위치 이동 완료");
    }

    /// <summary>Step 7: Camera QR 인식 → offset을 Cobot AI에 전달.
    /// 정확도 개선을 위해 멀티샘플 + median 사용 (단일 프레임 jitter / outlier 제거).
    /// </summary>
    private async Task Step_CameraQrRead(AmrCommand command, CancellationToken ct)
    {
        // 안정화: 카메라가 코봇 정지 후 새 프레임을 잡을 시간 확보
        const int StabilizationDelayMs = 200;

        // 멀티샘플: 카메라 15fps 환경에서 100ms 간격 5회 = ~400ms 동안 7~8 프레임 커버
        const int SampleCount = 5;
        const int SampleIntervalMs = 100;

        AddLog(SequenceStep.CameraQrRead,
            $"Camera QR 인식 시작 — 안정화 {StabilizationDelayMs}ms 후 {SampleCount}회 샘플링 ({SampleIntervalMs}ms 간격)");

        await Task.Delay(StabilizationDelayMs, ct);

        // 샘플 수집 (감지된 것만)
        var samples = new List<QrDetectionResult>();
        for (int i = 0; i < SampleCount; i++)
        {
            var r = _cameraService.GetQrDetectionResult();
            if (r.Detected) samples.Add(r);
            if (i < SampleCount - 1) await Task.Delay(SampleIntervalMs, ct);
        }

        QrDetectionResult qrResult;

        if (samples.Count == 0)
        {
            // 전체 미감지 → 마지막 정상값 fallback
            qrResult = _cameraService.GetQrDetectionResult();
            AddLog(SequenceStep.CameraQrRead,
                $"QR 샘플 {SampleCount}/{SampleCount} 전체 미감지 — 마지막 정상값 사용: " +
                $"dx={qrResult.RealDeltaXMm:F2}mm, dy={qrResult.RealDeltaYMm:F2}mm, angle={qrResult.RotationAngle:F2}°",
                isError: true);
        }
        else
        {
            // median 계산 — outlier 에 강한 통계량
            var xs = samples.Select(s => s.RealDeltaXMm).OrderBy(v => v).ToArray();
            var ys = samples.Select(s => s.RealDeltaYMm).OrderBy(v => v).ToArray();
            var angles = samples.Select(s => s.RotationAngle).OrderBy(v => v).ToArray();

            double Median(double[] arr) => arr.Length % 2 == 1
                ? arr[arr.Length / 2]
                : (arr[arr.Length / 2 - 1] + arr[arr.Length / 2]) / 2.0;

            qrResult = new QrDetectionResult
            {
                Detected = true,
                RealDeltaXMm = Median(xs),
                RealDeltaYMm = Median(ys),
                RotationAngle = Median(angles)
            };

            // 분산 정보 (튜닝/디버그용) — 표준편차 = sample spread 지표
            double Stddev(double[] arr)
            {
                var mean = arr.Average();
                return Math.Sqrt(arr.Average(v => (v - mean) * (v - mean)));
            }
            var sx = Stddev(xs);
            var sy = Stddev(ys);
            var sAng = Stddev(angles);

            AddLog(SequenceStep.CameraQrRead,
                $"QR 샘플 {samples.Count}/{SampleCount} 유효 — median: " +
                $"dx={qrResult.RealDeltaXMm:F2}mm (σ={sx:F2}), " +
                $"dy={qrResult.RealDeltaYMm:F2}mm (σ={sy:F2}), " +
                $"angle={qrResult.RotationAngle:F2}° (σ={sAng:F2})");
        }

        // ★ offset 합산 — ACS·Fairino 무수정, AMR.Web 에서 QR offset 에 더함.
        //   (1) PortOffset: 노드+슬롯별 (설비포트 공유 티칭 → 노드/슬롯 systematic bias)
        //   (2) ModelOffset: 모델별 LOAD/UNLOAD 공통 (노드·포트 무관)
        var rawDx = qrResult.RealDeltaXMm;
        var rawDy = qrResult.RealDeltaYMm;
        var rawDrz = qrResult.RotationAngle;

        var portOffset = _portOffset.GetOffset(command.NodeId, command.Port);
        var (mDx, mDy, mDrz) = _modelOffset.GetForJob(command.Model, command.JobType);

        var adjDx = rawDx + portOffset.OffsetDx + mDx;
        var adjDy = rawDy + portOffset.OffsetDy + mDy;
        var adjDrz = rawDrz + portOffset.OffsetDrz + mDrz;

        if (portOffset.OffsetDx != 0 || portOffset.OffsetDy != 0 || portOffset.OffsetDrz != 0)
        {
            AddLog(SequenceStep.CameraQrRead,
                $"PortOffset 적용 (Node={command.NodeId}, Port={command.Port ?? "-"}): " +
                $"dx +{portOffset.OffsetDx:F1}, dy +{portOffset.OffsetDy:F1}, drz +{portOffset.OffsetDrz:F2}");
        }
        if (mDx != 0 || mDy != 0 || mDrz != 0)
        {
            AddLog(SequenceStep.CameraQrRead,
                $"ModelOffset 적용 (Model={command.Model ?? "-"}, Job={command.JobType ?? "-"}): " +
                $"dx +{mDx:F1}, dy +{mDy:F1}, drz +{mDrz:F2}");
        }

        // offset 값을 Cobot AI 레지스터에 전달 (mm 단위, short → ushort 비트 변환)
        // dTheta 는 절대 각도 * 100 (0.01° 단위), 카메라 atan2 결과라 ±18000 범위.
        // Cobot todrz 함수가 절대값을 reference 기준 delta 로 변환하므로 여기서는 clamp 안 함.
        var dx = (short)Math.Clamp((int)Math.Round(adjDx), short.MinValue, short.MaxValue);
        var dy = (short)Math.Clamp((int)Math.Round(adjDy), short.MinValue, short.MaxValue);
        var dTheta = (short)Math.Clamp((int)Math.Round(adjDrz * 100), short.MinValue, short.MaxValue);

        AddLog(SequenceStep.CameraQrRead, $"Cobot AI0(dx)={dx}mm 쓰기");
        await _cobotService.WriteAnalogInputAsync(0, unchecked((ushort)dx), ct);  // AI0: dx

        AddLog(SequenceStep.CameraQrRead, $"Cobot AI1(dy)={dy}mm 쓰기");
        await _cobotService.WriteAnalogInputAsync(1, unchecked((ushort)dy), ct);  // AI1: dy

        AddLog(SequenceStep.CameraQrRead, $"Cobot AI2(dTheta)={dTheta} (0.01° 단위) 쓰기");
        await _cobotService.WriteAnalogInputAsync(2, unchecked((ushort)dTheta), ct); // AI2: dTheta

        AddLog(SequenceStep.CameraQrRead,
            $"QR offset 전달 완료: dx={dx}mm, dy={dy}mm, dTheta={dTheta} (Detected={qrResult.Detected})");
    }

    /// <summary>Step 8: PICK 수행 — JobType/PortType/Port/AmrSlot에 따라 DI 결정.
    /// 자재포트 UNLOAD 인 경우엔 PICK 전에 depth 카메라로 매거진 존재 여부를 슬롯별로 확인하고
    /// 발견된 슬롯에서만 PICK (DI20/DI21 검사 위치 → DI14/DI15 PICK).
    /// </summary>
    private async Task Step_CobotPickup(AmrCommand command, CancellationToken ct)
    {
        // DI 매핑:
        //   AMR PICK: DI0~3 (슬롯1~4)       AMR PLACE: DI4~7 (슬롯1~4)
        //   설비포트 PLACE: DI8~9 (슬롯1~2)  설비포트 PICK: DI10~11 (슬롯1~2)
        //   자재포트 PLACE: DI12~13 (슬롯1~2) 자재포트 PICK: DI14~15 (슬롯1~2)
        //   자재포트 slot1 검사: DI20         자재포트 slot2 검사: DI21
        // LEFT → 슬롯1, RIGHT → 슬롯2
        var isLoad = string.Equals(command.JobType, "LOAD", StringComparison.OrdinalIgnoreCase);
        var isFacility = command.PortType?.Contains("EQP", StringComparison.OrdinalIgnoreCase) ?? false;
        var portSlotOffset = string.Equals(command.Port, "RIGHT", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var amrSlotOffset = Math.Clamp(command.AmrSlot, 1, 4) - 1;

        ushort pickDiIndex;
        string pickTarget;

        if (isLoad)
        {
            // ★ 순서 중요 — AMR 슬롯 센서 검증을 자재포트 검사 전에 먼저!
            //
            // 이유: 자재포트 검사 위치(DI22/DI23) 에서는 코봇 팔이 AMR 슬롯 센서 위를 차폐할 수 있어
            //       센서가 false 로 잘못 읽힘. 검증을 먼저 하면 코봇이 아직 QR 위치(DI17, 안전)에
            //       있을 때 센서 읽으므로 차폐 없음.
            // 부가 이점: AMR 슬롯이 빈 상태면 어차피 PICK 불가 → 자재포트 검사 자체도 불필요.
            var amrSlot = Math.Clamp(command.AmrSlot, 1, 4);
            VerifyAmrSlotOccupied(amrSlot);

            // LOAD + 자재포트 — 그 다음 자재포트 빈 슬롯 탐색 (depth)
            //   DI22(slot1 검사) → 빈 슬롯? → Port=LEFT (PLACE 단계 DI12)
            //   점유 시 DI23(slot2 검사) → 빈 슬롯? → Port=RIGHT (PLACE 단계 DI13)
            //   둘 다 점유 → 알람+abort
            if (!isFacility)
            {
                await FindEmptyMaterialPortSlotForPlace(command, ct);
            }

            pickDiIndex = (ushort)(0 + amrSlotOffset);
            pickTarget = $"AMR PICK slot {amrSlot}";
        }
        else if (!isFacility)
        {
            // ★ UNLOAD + 자재포트 — depth 기반 슬롯 자동 탐색 후 PICK
            //   DI20(slot1 검사) → 발견 시 즉시 DI14(PICK slot1)
            //   미발견 시 DI21(slot2 검사) → 발견 시 DI15(PICK slot2)
            //   둘 다 미발견 → 알람+abort
            await PickFromMaterialPortWithDepthCheck(command, ct);
            AddLog(SequenceStep.CobotPickup, "PICK 완료");
            return;
        }
        else
        {
            // UNLOAD + 설비포트: 기존 그대로 — 지정 슬롯에서 PICK
            var basePickDi = 10;
            pickDiIndex = (ushort)(basePickDi + portSlotOffset);
            pickTarget = $"설비포트 PICK slot {portSlotOffset + 1}";
        }

        AddLog(SequenceStep.CobotPickup, $"PICK 시작 (DI{pickDiIndex}, {pickTarget})");
        await SendCobotCommandAndWaitAsync(pickDiIndex, $"PICK ({pickTarget})", ct);
        AddLog(SequenceStep.CobotPickup, "PICK 완료");
    }

    /// <summary>
    /// 자재포트 LOAD 전용 — AMR 에서 매거진 들기 전에, 자재포트 어느 슬롯이 비어있는지 depth 로 확인.
    /// 흐름:
    ///   DI22 (LOAD용 slot 1 검사 위치) → depth 검사 → 빈 슬롯(Detected=false) 이면 Port=LEFT 설정
    ///   slot 1 점유 시 DI23 (LOAD용 slot 2 검사 위치) → 빈 슬롯이면 Port=RIGHT 설정
    ///   둘 다 점유 → 알람 ERR-113 + ACS abnormal + abort (AMR PICK 진행 안 함)
    /// 결정된 슬롯은 command.Port 에 반영 — 후속 Step_CobotPlace 가 DI12/DI13 로 PLACE.
    ///
    /// 주의: UNLOAD 의 DI20/DI21 과 다름 — LOAD 는 빈 슬롯에 놓기 위한 별도 검사 위치 사용.
    /// </summary>
    private async Task FindEmptyMaterialPortSlotForPlace(AmrCommand command, CancellationToken ct)
    {
        // 1) Slot 1 검사 위치 (DI22)
        AddLog(SequenceStep.CobotPickup, "자재포트 PLACE 대상 탐색 — slot 1 검사 위치 이동 (DI22)");
        await SendCobotCommandAndWaitAsync(22, "자재포트 LOAD slot 1 검사 위치", ct);
        await Task.Delay(500, ct);

        var r1 = _magazineDetection.Detect();
        AddLog(SequenceStep.CobotPickup,
            $"slot 1 depth 검사: Detected={r1.Detected}, in-range={r1.ValidPixelRatio:P1}, avg={r1.AverageDepthMm}mm — {r1.Reason}");

        // LOAD 케이스는 "빈 슬롯" 을 찾는 거 — Detected=false (매거진 없음) 가 우리가 원하는 상태
        if (!r1.Detected)
        {
            command.Port = "LEFT";
            AddLog(SequenceStep.CobotPickup, "자재포트 slot 1 비어있음 — PLACE 대상: LEFT");
            return;
        }

        // 2) Slot 1 점유 → Slot 2 검사 위치 (DI23)
        AddLog(SequenceStep.CobotPickup, "slot 1 점유 — slot 2 검사 위치 이동 (DI23)");
        await SendCobotCommandAndWaitAsync(23, "자재포트 LOAD slot 2 검사 위치", ct);
        await Task.Delay(500, ct);

        var r2 = _magazineDetection.Detect();
        AddLog(SequenceStep.CobotPickup,
            $"slot 2 depth 검사: Detected={r2.Detected}, in-range={r2.ValidPixelRatio:P1}, avg={r2.AverageDepthMm}mm — {r2.Reason}");

        if (!r2.Detected)
        {
            command.Port = "RIGHT";
            AddLog(SequenceStep.CobotPickup, "자재포트 slot 2 비어있음 — PLACE 대상: RIGHT");
            return;
        }

        // 3) 둘 다 점유 → 알람 + ACS 보고 + abort
        AddLog(SequenceStep.CobotPickup,
            "자재포트 slot 1, 2 모두 점유 — PLACE 불가, 알람 발생 후 시퀀스 중단", isError: true);

        ReportAbnormalToAcs("MATERIAL_PORT_FULL", "MATERIALPORT");
        throw CreateAbortException(Alarm.MaterialPortFull,
            "자재포트 slot 1, 2 모두 매거진 점유 — 빈 슬롯 없음");
    }

    /// <summary>
    /// 자재포트 UNLOAD 전용 — depth 카메라로 슬롯 1/2 매거진 존재 여부 확인 후 발견한 슬롯에서 PICK.
    /// 흐름:
    ///   DI20 (자재포트 slot 1 검사 위치) → depth 검사 → 검출 시 DI14 (자재포트 PICK slot 1)
    ///   미검출 시 DI21 (slot 2 검사 위치) → depth 검사 → 검출 시 DI15 (PICK slot 2)
    ///   둘 다 미검출 → 알람 ERR-112 + ACS abnormal 보고 + 시퀀스 abort
    /// 결정된 슬롯은 command.Port 에 반영 (LEFT/RIGHT) — 후속 처리/응답에서 동일 슬롯 참조.
    /// </summary>
    private async Task PickFromMaterialPortWithDepthCheck(AmrCommand command, CancellationToken ct)
    {
        // 1) Slot 1 검사 위치 (DI20)
        AddLog(SequenceStep.CobotPickup, "자재포트 slot 1 검사 위치 이동 (DI20)");
        await SendCobotCommandAndWaitAsync(20, "자재포트 slot 1 검사 위치", ct);
        await Task.Delay(500, ct);   // depth frame 안정화

        var r1 = _magazineDetection.Detect();
        AddLog(SequenceStep.CobotPickup,
            $"slot 1 depth 검사: Detected={r1.Detected}, in-range={r1.ValidPixelRatio:P1}, avg={r1.AverageDepthMm}mm — {r1.Reason}");

        if (r1.Detected)
        {
            command.Port = "LEFT";
            AddLog(SequenceStep.CobotPickup, "자재포트 slot 1 매거진 감지 — PICK slot 1 (DI14)");
            await SendCobotCommandAndWaitAsync(14, "자재포트 PICK slot 1", ct);
            return;
        }

        // 2) Slot 2 검사 위치 (DI21)
        AddLog(SequenceStep.CobotPickup, "slot 1 매거진 없음 — slot 2 검사 위치 이동 (DI21)");
        await SendCobotCommandAndWaitAsync(21, "자재포트 slot 2 검사 위치", ct);
        await Task.Delay(500, ct);

        var r2 = _magazineDetection.Detect();
        AddLog(SequenceStep.CobotPickup,
            $"slot 2 depth 검사: Detected={r2.Detected}, in-range={r2.ValidPixelRatio:P1}, avg={r2.AverageDepthMm}mm — {r2.Reason}");

        if (r2.Detected)
        {
            command.Port = "RIGHT";
            AddLog(SequenceStep.CobotPickup, "자재포트 slot 2 매거진 감지 — PICK slot 2 (DI15)");
            await SendCobotCommandAndWaitAsync(15, "자재포트 PICK slot 2", ct);
            return;
        }

        // 3) 둘 다 매거진 없음 → 알람 + ACS 보고 + abort
        AddLog(SequenceStep.CobotPickup,
            "자재포트 slot 1, 2 모두 매거진 없음 — 알람 발생, ACS 보고 후 시퀀스 중단", isError: true);

        ReportAbnormalToAcs("MATERIAL_PORT_EMPTY", "MATERIALPORT");
        throw CreateAbortException(Alarm.MaterialPortEmpty,
            "자재포트 slot 1, 2 모두 매거진 없음 — depth 검사 실패");
    }

    /// <summary>
    /// LOAD 시 AMR 슬롯에서 PICK 하기 전 — 그 슬롯 센서가 ON(매거진 있음)인지 확인.
    /// OFF 면 알람 발생시키고 시퀀스 중단 → ACS 로 abnormal 보고.
    /// </summary>
    private void VerifyAmrSlotOccupied(int amrSlot)
    {
        var inputs = _ioModuleService.CurrentInputs
            ?? throw CreateAbortException(Alarm.AmrSlotEmpty,
                $"AMR PICK slot {amrSlot} 검증 실패 — I/O 모듈 입력 미수신");

        bool occupied = amrSlot switch
        {
            1 => inputs.MzDetect1,
            2 => inputs.MzDetect2,
            3 => inputs.MzDetect3,
            4 => inputs.MzDetect4,
            _ => false
        };

        if (!occupied)
        {
            AddLog(SequenceStep.CobotPickup,
                $"AMR slot {amrSlot} 매거진 없음 (센서 OFF) — PICK 중단, 알람 발생", isError: true);

            // Node 를 PORT{N} 으로 — IoModuleService 의 자동 해제 로직 활용 (센서 ON 되면 abnormal 해제)
            ReportAbnormalToAcs("MAGAZINE_MISSING", $"PORT{amrSlot}");
            throw CreateAbortException(Alarm.AmrSlotEmpty,
                $"AMR slot {amrSlot} 매거진 없음 — 이전 UNLOAD 가 정상 처리됐는지 확인 필요");
        }

        AddLog(SequenceStep.CobotPickup, $"AMR slot {amrSlot} 매거진 확인 (센서 ON) — PICK 진행");
    }

    /// <summary>Step 9: PLACE 수행 — JobType/PortType/Port/AmrSlot에 따라 DI 결정</summary>
    private async Task Step_CobotPlace(AmrCommand command, CancellationToken ct)
    {
        var isLoad = string.Equals(command.JobType, "LOAD", StringComparison.OrdinalIgnoreCase);
        // PortType 에 "EQP" 가 포함되어 있으면 설비포트, 아니면 자재포트
        var isFacility = command.PortType?.Contains("EQP", StringComparison.OrdinalIgnoreCase) ?? false;
        var portSlotOffset = string.Equals(command.Port, "RIGHT", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        ushort placeDiIndex;
        string placeTarget;

        if (isLoad)
        {
            // LOAD: 설비/자재포트에 PLACE
            var basePlaceDi = isFacility ? 8 : 12;
            var portKind = isFacility ? "설비포트" : "자재포트";
            placeDiIndex = (ushort)(basePlaceDi + portSlotOffset);
            placeTarget = $"{portKind} PLACE slot {portSlotOffset + 1}";
        }
        else
        {
            // UNLOAD: AMR 에 PLACE — 자동 빈 슬롯 선택 (1→2→3→4)
            //   1번부터 차례로 검사해서 가장 먼저 비어있는 슬롯(센서 OFF)에 놓음
            //   4 슬롯 모두 가득 차있으면 알람 발생 + ACS 보고 후 시퀀스 중단
            var selectedSlot = SelectEmptyAmrSlot();

            // 선택된 슬롯을 command.AmrSlot 에 반영 — 후속 단계/로그/응답에서 동일 슬롯 참조
            command.AmrSlot = selectedSlot;

            var amrSlotOffset = selectedSlot - 1;
            placeDiIndex = (ushort)(4 + amrSlotOffset);
            placeTarget = $"AMR PLACE slot {selectedSlot} (자동 선택)";
        }

        AddLog(SequenceStep.CobotPlace, $"PLACE 시작 (DI{placeDiIndex}, {placeTarget})");
        await SendCobotCommandAndWaitAsync(placeDiIndex, $"PLACE ({placeTarget})", ct);
        AddLog(SequenceStep.CobotPlace, "PLACE 완료");
    }

    /// <summary>
    /// UNLOAD 시 AMR 에 PLACE 할 빈 슬롯을 1→2→3→4 순서로 찾는다.
    /// 모두 차있으면 알람 + ACS 보고 후 예외 throw.
    /// </summary>
    private int SelectEmptyAmrSlot()
    {
        var inputs = _ioModuleService.CurrentInputs
            ?? throw CreateAbortException(Alarm.AmrSlotsFull,
                "AMR PLACE 슬롯 선택 실패 — I/O 모듈 입력 미수신");

        // 센서 ON = 매거진 있음(occupied) → 그 슬롯 건너뜀
        // 센서 OFF = 비어있음 → 그 슬롯에 PLACE
        if (!inputs.MzDetect1) return Found(1);
        if (!inputs.MzDetect2) return Found(2);
        if (!inputs.MzDetect3) return Found(3);
        if (!inputs.MzDetect4) return Found(4);

        // 모든 슬롯 가득 참 → 알람 + ACS 보고
        AddLog(SequenceStep.CobotPlace,
            "AMR 슬롯 1~4 모두 가득 참 — PLACE 불가, 알람 발생", isError: true);

        ReportAbnormalToAcs("AMR_SLOTS_FULL", "AMRSLOT");
        throw CreateAbortException(Alarm.AmrSlotsFull,
            "AMR 슬롯 1~4 모두 매거진 점유 중 — 빈 슬롯 없음");

        int Found(int slot)
        {
            AddLog(SequenceStep.CobotPlace,
                $"빈 AMR slot {slot} 선택 (1:{Show(inputs.MzDetect1)} 2:{Show(inputs.MzDetect2)} 3:{Show(inputs.MzDetect3)} 4:{Show(inputs.MzDetect4)})");

            // 이전에 SLOTS_FULL abnormal 이 있었다면 해제 — 빈 슬롯 확보됐으므로
            if (_ioModuleService.CurrentAbnormal?.Type == "AMR_SLOTS_FULL")
                _ioModuleService.ClearAbnormal();

            return slot;
        }

        static string Show(bool on) => on ? "Full" : "Empty";
    }

    /// <summary>알람 발생 + 시퀀스 중단을 위한 예외 생성</summary>
    private OperationCanceledException CreateAbortException(Alarm alarm, string detail)
    {
        _logger.LogWarning("[Sequence Abort] {AlarmId} {AlarmName} — {Detail}", alarm.Id, alarm.Name, detail);
        AbortWithAlarm(alarm);
        return new OperationCanceledException($"[{alarm.Id}] {alarm.Name} — {detail}");
    }

    /// <summary>
    /// ACS 로 abnormal 보고 — IoModuleService 가 이미 가진 CARRIER_REMOVED 보고 메커니즘과
    /// 동일하게, AbnormalInfo 를 설정하면 MainSequenceService 가 다음 status publish 시 포함.
    /// </summary>
    private void ReportAbnormalToAcs(string type, string node)
    {
        _ioModuleService.SetAbnormal(new AbnormalInfo
        {
            Type = type,
            Node = node,
            Timestamp = DateTime.UtcNow.ToString("o")
        });
    }

    /// <summary>Step 10: 완료 통보, Idle 복귀</summary>
    private async Task Step_Complete(AmrCommand command, CancellationToken ct)
    {
        // Cobot Home 위치 이동 (DI25)
        AddLog(SequenceStep.Complete, "Cobot Home 위치 이동");
        await SendCobotCommandAndWaitAsync(25, "Home 위치 이동", ct);

        // MQTT로 완료 메시지 전송
        var reply = new CommandReply
        {
            CmdId = command.CmdId,
            JobType = command.JobType,
            Status = "COMPLETED",
            ResultCode = 0,
            Message = $"시퀀스 완료: {command.NodeId}",
            Timestamp = DateTime.UtcNow.ToString("o")
        };
        await _mqttService.PublishReplyAsync(reply, ct);

        State.CurrentStep = SequenceStep.Idle;
        AddLog(SequenceStep.Complete, "시퀀스 완료 — Idle 복귀");
    }

    /// <summary>
    /// CHARGE 작업 전용 완료 처리 — Cobot 은 이미 Phome 에 있으므로 DI25 재호출 없이
    /// COMPLETED reply 만 전송하고 Idle 복귀.
    /// </summary>
    private async Task CompleteChargeAsync(AmrCommand command, CancellationToken ct)
    {
        State.CurrentStep = SequenceStep.Complete;
        State.StepStartedAt = DateTime.Now;
        AddLog(SequenceStep.Complete, "CHARGE 시퀀스 완료 처리");

        var reply = new CommandReply
        {
            CmdId = command.CmdId,
            JobType = command.JobType,
            Status = "COMPLETED",
            ResultCode = 0,
            Message = $"CHARGE 완료: {command.NodeId}",
            Timestamp = DateTime.UtcNow.ToString("o")
        };
        await _mqttService.PublishReplyAsync(reply, ct);

        State.CurrentStep = SequenceStep.Idle;
        AddLog(SequenceStep.Complete, $"CHARGE 시퀀스 완료 — Idle 복귀 ({command.NodeId})");
    }

    #endregion

    #region 헬퍼

    /// <summary>Cobot DI 명령 전송 후 DO0(Busy) 확인 → DI OFF → DO1(Complete) 또는 DO2(Error) 대기</summary>
    private async Task SendCobotCommandAndWaitAsync(ushort diIndex, string description, CancellationToken ct)
    {
        if (!_cobotService.IsConnected)
            throw new InvalidOperationException($"Cobot 미연결 상태에서 명령 시도: {description}");

        // DI ON — 명령 전송
        await _cobotService.WriteDigitalInputAsync(diIndex, true, ct);

        var deadline = DateTime.Now.AddSeconds(CobotTimeoutSeconds);

        try
        {
            // Phase 1: DO0(Busy) 대기 — Cobot이 명령을 수신했는지 확인
            while (!ct.IsCancellationRequested)
            {
                if (DateTime.Now > deadline)
                    throw new TimeoutException($"Cobot Busy 대기 타임아웃 ({CobotTimeoutSeconds}초): {description}");

                var dos = await _cobotService.ReadDigitalOutputsAsync(0, 3, ct);

                if (dos[2]) // DO2: Error
                    throw new Exception($"Cobot 에러 발생: {description}");

                if (dos[0]) // DO0: Busy — 명령 수신 확인
                    break;

                await Task.Delay(PollIntervalMs, ct);
            }

            ct.ThrowIfCancellationRequested();

            // Busy 확인 → 명령 DI OFF
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

                if (dos[2]) // DO2: Error
                    throw new Exception($"Cobot 에러 발생: {description}");

                if (dos[1]) // DO1: Complete
                    break;

                await Task.Delay(PollIntervalMs, ct);
            }

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            // 안전장치: Phase 1에서 예외 발생 시에도 DI OFF 보장
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

    private void AddLog(SequenceStep step, string message, bool isError = false)
    {
        _logs.Enqueue(new SequenceLogEntry(DateTime.Now, step, message, isError));

        // 최대 로그 수 초과 시 오래된 로그 제거
        while (_logs.Count > MaxLogEntries)
            _logs.TryDequeue(out _);

        if (isError)
            _logger.LogWarning("[Sequence Step {Step}] {Message}", step, message);
        else
            _logger.LogInformation("[Sequence Step {Step}] {Message}", step, message);
    }

    #endregion
}
