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
    private readonly IDbContextFactory<AmrDbContext> _dbFactory;
    private readonly ILogger<MoveSequenceRunner> _logger;

    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly ConcurrentQueue<SequenceLogEntry> _logs = new();
    private CancellationTokenSource? _sequenceCts;
    private CancellationTokenSource? _demoCts;

    private const int MaxLogEntries = 200;
    private const int ArrivalTimeoutSeconds = 120;
    private const int CobotTimeoutSeconds = 60;
    private const int PollIntervalMs = 500;

    public MoveSequenceRunner(
        AmrService amrService,
        CobotService cobotService,
        MqttService mqttService,
        CameraService cameraService,
        IDbContextFactory<AmrDbContext> dbFactory,
        ILogger<MoveSequenceRunner> logger)
    {
        _amrService = amrService;
        _cobotService = cobotService;
        _mqttService = mqttService;
        _cameraService = cameraService;
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

            // Step 1: MoveCmdReceived
            await ExecuteStepInternalAsync(SequenceStep.MoveCmdReceived, command, token);

            // Step 2: MoveCmdReply
            await ExecuteStepInternalAsync(SequenceStep.MoveCmdReply, command, token);

            // Step 3: SendMoveCommand
            await ExecuteStepInternalAsync(SequenceStep.SendMoveCommand, command, token);

            // Step 4: WaitArrival
            await ExecuteStepInternalAsync(SequenceStep.WaitArrival, command, token);

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
            AddLog(State.CurrentStep, "시퀀스 취소됨", true);
            State.CurrentStep = SequenceStep.Idle;
            _logger.LogWarning("시퀀스 취소됨");
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
                await Step_WaitArrival(ct);
                break;
            case SequenceStep.WaitActionCmd:
                await Step_WaitActionCmd(command, ct);
                break;
            case SequenceStep.CobotQrPosition:
                await Step_CobotQrPosition(command, ct);
                break;
            case SequenceStep.CameraQrRead:
                await Step_CameraQrRead(ct);
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
    private async Task Step_WaitArrival(CancellationToken ct)
    {
        AddLog(SequenceStep.WaitArrival, "AMR 도착 대기 시작");

        var deadline = DateTime.Now.AddSeconds(ArrivalTimeoutSeconds);

        // Phase 1: RobotState가 Started가 될 때까지 대기 (이동 시작 확인)
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

        // Phase 2: RobotState가 Stopped가 될 때까지 대기 (이동 완료 확인)
        while (!ct.IsCancellationRequested)
        {
            if (DateTime.Now > deadline)
                throw new TimeoutException($"AMR 도착 대기 타임아웃 ({ArrivalTimeoutSeconds}초)");

            var status = await _amrService.ReadStatusAsync(ct);

            if (status.RobotState == RobotState.Stopped)
            {
                AddLog(SequenceStep.WaitArrival, "AMR 도착 완료 (RobotState=Stopped)");
                return;
            }

            await Task.Delay(PollIntervalMs, ct);
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Step 5: ActionCmd 대기 — 설비포트면 대기, 자재포트면 스킵</summary>
    private async Task Step_WaitActionCmd(AmrCommand command, CancellationToken ct)
    {
        var isFacility = string.Equals(command.PortType, "FACILITY", StringComparison.OrdinalIgnoreCase);

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
                    $"ActionCmd 수신 완료 (CmdId={actionCmd.CmdId})");
                return;
            }

            await Task.Delay(PollIntervalMs, ct);
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Step 6: Cobot을 QR 코드 읽기 위치로 이동</summary>
    private async Task Step_CobotQrPosition(AmrCommand command, CancellationToken ct)
    {
        // TODO: 자재포트 티칭 완료 후 DI17 분기 추가
        // 현재는 설비/자재 모두 설비포트 QR 위치(DI16) 사용
        ushort qrDiIndex = 16;

        AddLog(SequenceStep.CobotQrPosition, $"Cobot QR 읽기 위치 이동 (DI{qrDiIndex}, 설비포트)");
        await SendCobotCommandAndWaitAsync(qrDiIndex, "QR 읽기 위치 이동 (설비포트)", ct);
        AddLog(SequenceStep.CobotQrPosition, "Cobot QR 읽기 위치 이동 완료");
    }

    /// <summary>Step 7: Camera QR 인식 → offset을 Cobot AI에 전달</summary>
    private async Task Step_CameraQrRead(CancellationToken ct)
    {
        AddLog(SequenceStep.CameraQrRead, "Camera QR 인식 시작");

        var qrResult = _cameraService.GetQrDetectionResult();

        AddLog(SequenceStep.CameraQrRead,
            $"Camera 원시값: Detected={qrResult.Detected}, RealDeltaX={qrResult.RealDeltaXMm:F2}mm, RealDeltaY={qrResult.RealDeltaYMm:F2}mm, Rotation={qrResult.RotationAngle:F2}°");

        if (!qrResult.Detected)
        {
            AddLog(SequenceStep.CameraQrRead, "QR 미감지 — offset (0, 0, 0) 전달", true);
        }

        // offset 값을 Cobot AI 레지스터에 전달 (mm 단위, short → ushort 비트 변환)
        var dx = (short)Math.Clamp((int)qrResult.RealDeltaXMm, short.MinValue, short.MaxValue);
        var dy = (short)Math.Clamp((int)qrResult.RealDeltaYMm, short.MinValue, short.MaxValue);
        var dTheta = (short)Math.Clamp((int)(qrResult.RotationAngle * 100), short.MinValue, short.MaxValue);

        AddLog(SequenceStep.CameraQrRead, $"Cobot AI0(dx)={dx}mm 쓰기");
        await _cobotService.WriteAnalogInputAsync(0, unchecked((ushort)dx), ct);  // AI0: dx

        AddLog(SequenceStep.CameraQrRead, $"Cobot AI1(dy)={dy}mm 쓰기");
        await _cobotService.WriteAnalogInputAsync(1, unchecked((ushort)dy), ct);  // AI1: dy

        AddLog(SequenceStep.CameraQrRead, $"Cobot AI2(dTheta)={dTheta} (0.01° 단위) 쓰기");
        await _cobotService.WriteAnalogInputAsync(2, unchecked((ushort)dTheta), ct); // AI2: dTheta

        AddLog(SequenceStep.CameraQrRead,
            $"QR offset 전달 완료: dx={dx}mm, dy={dy}mm, dTheta={dTheta} (Detected={qrResult.Detected})");
    }

    /// <summary>Step 8: PICK 수행 — JobType/PortType/Port/AmrSlot에 따라 DI 결정</summary>
    private async Task Step_CobotPickup(AmrCommand command, CancellationToken ct)
    {
        // DI 매핑:
        //   AMR PICK: DI0~3 (슬롯1~4)       AMR PLACE: DI4~7 (슬롯1~4)
        //   설비포트 PLACE: DI8~9 (슬롯1~2)  설비포트 PICK: DI10~11 (슬롯1~2)
        //   자재포트 PLACE: DI12~13 (슬롯1~2) 자재포트 PICK: DI14~15 (슬롯1~2)
        // LEFT → 슬롯1, RIGHT → 슬롯2
        var isLoad = string.Equals(command.JobType, "LOAD", StringComparison.OrdinalIgnoreCase);
        var isFacility = string.Equals(command.PortType, "FACILITY", StringComparison.OrdinalIgnoreCase);
        var portSlotOffset = string.Equals(command.Port, "RIGHT", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var amrSlotOffset = Math.Clamp(command.AmrSlot, 1, 4) - 1;

        ushort pickDiIndex;
        string pickTarget;

        if (isLoad)
        {
            // LOAD: AMR에서 PICK → DI0 + amrSlotOffset
            pickDiIndex = (ushort)(0 + amrSlotOffset);
            pickTarget = $"AMR PICK slot {command.AmrSlot}";
        }
        else
        {
            // UNLOAD: 설비/자재포트에서 PICK
            // TODO: 자재포트 티칭 완료 후 DI14~15 분기 추가
            // 현재는 설비/자재 모두 설비포트 PICK DI(DI10~11) 사용
            pickDiIndex = (ushort)(10 + portSlotOffset);
            pickTarget = $"설비포트 PICK slot {portSlotOffset + 1}";
        }

        AddLog(SequenceStep.CobotPickup, $"PICK 시작 (DI{pickDiIndex}, {pickTarget})");
        await SendCobotCommandAndWaitAsync(pickDiIndex, $"PICK ({pickTarget})", ct);
        AddLog(SequenceStep.CobotPickup, "PICK 완료");
    }

    /// <summary>Step 9: PLACE 수행 — JobType/PortType/Port/AmrSlot에 따라 DI 결정</summary>
    private async Task Step_CobotPlace(AmrCommand command, CancellationToken ct)
    {
        var isLoad = string.Equals(command.JobType, "LOAD", StringComparison.OrdinalIgnoreCase);
        var isFacility = string.Equals(command.PortType, "FACILITY", StringComparison.OrdinalIgnoreCase);
        var portSlotOffset = string.Equals(command.Port, "RIGHT", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var amrSlotOffset = Math.Clamp(command.AmrSlot, 1, 4) - 1;

        ushort placeDiIndex;
        string placeTarget;

        if (isLoad)
        {
            // LOAD: 설비/자재포트에 PLACE
            // TODO: 자재포트 티칭 완료 후 DI12~13 분기 추가
            // 현재는 설비/자재 모두 설비포트 PLACE DI(DI8~9) 사용
            placeDiIndex = (ushort)(8 + portSlotOffset);
            placeTarget = $"설비포트 PLACE slot {portSlotOffset + 1}";
        }
        else
        {
            // UNLOAD: AMR에 PLACE → DI4 + amrSlotOffset
            placeDiIndex = (ushort)(4 + amrSlotOffset);
            placeTarget = $"AMR PLACE slot {command.AmrSlot}";
        }

        AddLog(SequenceStep.CobotPlace, $"PLACE 시작 (DI{placeDiIndex}, {placeTarget})");
        await SendCobotCommandAndWaitAsync(placeDiIndex, $"PLACE ({placeTarget})", ct);
        AddLog(SequenceStep.CobotPlace, "PLACE 완료");
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
            Status = "COMPLETED",
            ResultCode = 0,
            Message = $"시퀀스 완료: {command.NodeId}",
            Timestamp = DateTime.UtcNow.ToString("o")
        };
        await _mqttService.PublishReplyAsync(reply, ct);

        State.CurrentStep = SequenceStep.Idle;
        AddLog(SequenceStep.Complete, "시퀀스 완료 — Idle 복귀");
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
