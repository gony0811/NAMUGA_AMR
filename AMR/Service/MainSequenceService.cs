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
    private readonly IDbContextFactory<AmrDbContext> _dbFactory;
    private readonly ILogger<MainSequenceService> _logger;

    public MainSequenceService(
        AmrService amrService,
        MqttService mqttService,
        MoveSequenceRunner sequenceRunner,
        AlarmService alarmService,
        IoModuleService ioModuleService,
        CobotService cobotService,
        IDbContextFactory<AmrDbContext> dbFactory,
        ILogger<MainSequenceService> logger)
    {
        _amrService = amrService;
        _mqttService = mqttService;
        _sequenceRunner = sequenceRunner;
        _alarmService = alarmService;
        _ioModuleService = ioModuleService;
        _cobotService = cobotService;
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
                // AMR + MQTT 모두 연결된 경우에만 상태 퍼블리시
                if (_amrService.IsConnected && _mqttService.IsConnected)
                {
                    var robotStatus = await _amrService.ReadStatusAsync(stoppingToken);

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

                    var alarm = await _alarmService.EvaluateAsync(stoppingToken);

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
        // 1. AMR 연결 상태 확인
        if (!_amrService.IsConnected)
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

        // 5. 시퀀스 실행 (Fire-and-forget: 시퀀스는 백그라운드에서 진행)
        _logger.LogInformation("Move 시퀀스 시작: NodeId={NodeId}, Port={Port}, JobType={JobType}",
            command.NodeId, command.Port, command.JobType);
        _ = _sequenceRunner.RunSequenceAsync(command, ct);
    }

    private async Task ReplyAsync(string cmdId, string status, int resultCode, string message, CancellationToken ct)
    {
        var reply = new CommandReply
        {
            CmdId = cmdId,
            Status = status,
            ResultCode = resultCode,
            Message = message,
            Timestamp = DateTime.UtcNow.ToString("o")
        };

        await _mqttService.PublishReplyAsync(reply, ct);
    }
}
