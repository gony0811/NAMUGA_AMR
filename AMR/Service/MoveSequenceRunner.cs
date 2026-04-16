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
                JobType = State.JobType
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
                await Step_CobotPlace(ct);
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

        AddLog(SequenceStep.MoveCmdReceived,
            $"MoveCmd 수신: NodeId={command.NodeId}, Port={command.Port ?? "없음"}, JobType={command.JobType ?? "없음"}");

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

    /// <summary>Step 4: AMR 도착 대기 — WorkStatus가 Idle이 될 때까지 polling</summary>
    private async Task Step_WaitArrival(CancellationToken ct)
    {
        AddLog(SequenceStep.WaitArrival, "AMR 도착 대기 시작");

        var deadline = DateTime.Now.AddSeconds(ArrivalTimeoutSeconds);

        while (!ct.IsCancellationRequested)
        {
            if (DateTime.Now > deadline)
                throw new TimeoutException($"AMR 도착 대기 타임아웃 ({ArrivalTimeoutSeconds}초)");

            var status = await _amrService.ReadStatusAsync(ct);

            if (status.WorkStatus == WorkStatus.Idle)
            {
                AddLog(SequenceStep.WaitArrival, "AMR 도착 완료 (WorkStatus=Idle)");
                return;
            }

            await Task.Delay(PollIntervalMs, ct);
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Step 5: ActionCmd 대기 — 초기 구현은 스킵 처리</summary>
    private Task Step_WaitActionCmd(AmrCommand command, CancellationToken ct)
    {
        var port = command.Port?.ToUpperInvariant();

        if (port is "LEFT" or "RIGHT")
        {
            // TODO: ActionCmd 수신 대기 구현 (TaskCompletionSource)
            AddLog(SequenceStep.WaitActionCmd,
                $"ActionCmd 대기 스킵 (Port={port}, 향후 구현 예정)");
        }
        else
        {
            AddLog(SequenceStep.WaitActionCmd,
                "Port 미지정 — ActionCmd 대기 없이 다음 단계 진행");
        }

        return Task.CompletedTask;
    }

    /// <summary>Step 6: Cobot을 QR 코드 읽기 위치로 이동</summary>
    private async Task Step_CobotQrPosition(AmrCommand command, CancellationToken ct)
    {
        // port 정보로 QR 스캔 위치 결정: DI16=설비포트, DI17=자재포트
        // 초기 구현: 설비포트(DI16) 기본 사용
        ushort qrDiIndex = 16; // 설비포트 QR 스캔

        AddLog(SequenceStep.CobotQrPosition, $"Cobot QR 읽기 위치 이동 (DI{qrDiIndex})");
        await SendCobotCommandAndWaitAsync(qrDiIndex, "QR 읽기 위치 이동", ct);
        AddLog(SequenceStep.CobotQrPosition, "Cobot QR 읽기 위치 이동 완료");
    }

    /// <summary>Step 7: Camera QR 인식 → offset을 Cobot AI에 전달</summary>
    private async Task Step_CameraQrRead(CancellationToken ct)
    {
        AddLog(SequenceStep.CameraQrRead, "Camera QR 인식 시작");

        var qrResult = _cameraService.GetQrDetectionResult();

        if (!qrResult.Detected)
        {
            AddLog(SequenceStep.CameraQrRead, "QR 미감지 — offset (0, 0, 0) 전달", true);
        }

        // offset 값을 Cobot AI 레지스터에 전달 (mm 단위, ushort 변환)
        var dx = (ushort)Math.Clamp((int)qrResult.RealDeltaXMm, 0, ushort.MaxValue);
        var dy = (ushort)Math.Clamp((int)qrResult.RealDeltaYMm, 0, ushort.MaxValue);
        var dTheta = (ushort)Math.Clamp((int)(qrResult.RotationAngle * 100), 0, ushort.MaxValue);

        await _cobotService.WriteAnalogInputAsync(0, dx, ct);  // AI0: dx
        await _cobotService.WriteAnalogInputAsync(1, dy, ct);  // AI1: dy
        await _cobotService.WriteAnalogInputAsync(2, dTheta, ct); // AI2: dTheta

        AddLog(SequenceStep.CameraQrRead,
            $"QR offset 전달: dx={dx}mm, dy={dy}mm, dTheta={dTheta} (Detected={qrResult.Detected})");
    }

    /// <summary>Step 8: port 위치에서 PICKUP 수행</summary>
    private async Task Step_CobotPickup(AmrCommand command, CancellationToken ct)
    {
        // 초기 구현: 설비포트 Loading slot 1 (DI8) 사용
        // 향후 jobType/port type에 따라 분기:
        //   설비포트 Loading: DI8~9
        //   설비포트 Unloading: DI10~11
        //   자재포트 Loading: DI12~13
        //   자재포트 Unloading: DI14~15
        ushort pickupDiIndex = 8; // 설비포트 Loading slot 1

        AddLog(SequenceStep.CobotPickup, $"PICKUP 시작 (DI{pickupDiIndex})");
        await SendCobotCommandAndWaitAsync(pickupDiIndex, "PICKUP", ct);
        AddLog(SequenceStep.CobotPickup, "PICKUP 완료");
    }

    /// <summary>Step 9: AMR Port 1에 PLACE 수행</summary>
    private async Task Step_CobotPlace(CancellationToken ct)
    {
        // DI4: AMR PLACE slot 1
        ushort placeDiIndex = 4;

        AddLog(SequenceStep.CobotPlace, $"PLACE 시작 (DI{placeDiIndex}, AMR Port 1)");
        await SendCobotCommandAndWaitAsync(placeDiIndex, "PLACE (AMR Port 1)", ct);
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

    /// <summary>Cobot DI 명령 전송 후 DO1(Complete) 또는 DO2(Error) 대기</summary>
    private async Task SendCobotCommandAndWaitAsync(ushort diIndex, string description, CancellationToken ct)
    {
        if (!_cobotService.IsConnected)
            throw new InvalidOperationException($"Cobot 미연결 상태에서 명령 시도: {description}");

        // DI ON
        await _cobotService.WriteDigitalInputAsync(diIndex, true, ct);

        var deadline = DateTime.Now.AddSeconds(CobotTimeoutSeconds);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (DateTime.Now > deadline)
                    throw new TimeoutException($"Cobot 응답 타임아웃 ({CobotTimeoutSeconds}초): {description}");

                // DO0=Busy, DO1=Complete, DO2=Error
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
            // DI OFF (에러 발생 시에도 반드시 OFF)
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
