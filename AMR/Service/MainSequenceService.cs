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
    private readonly IDbContextFactory<AmrDbContext> _dbFactory;
    private readonly ILogger<MainSequenceService> _logger;

    public MainSequenceService(
        AmrService amrService,
        MqttService mqttService,
        IDbContextFactory<AmrDbContext> dbFactory,
        ILogger<MainSequenceService> logger)
    {
        _amrService = amrService;
        _mqttService = mqttService;
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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // AMR + MQTT 모두 연결된 경우에만 상태 퍼블리시
                if (_amrService.IsConnected && _mqttService.IsConnected)
                {
                    var robotStatus = await _amrService.ReadStatusAsync(stoppingToken);
                    var statusMessage = AmrStatusMessage.FromRobotStatus(robotStatus);

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

        // 2. 현재 작업 상태 확인 (Idle 상태에서만 이동 명령 수행)
        var status = await _amrService.ReadStatusAsync(ct);
        if (status.WorkStatus != WorkStatus.Idle)
        {
            _logger.LogWarning("AMR이 작업 중(WorkStatus={WorkStatus})에 moveCmd 수신: NodeId={NodeId}",
                status.WorkStatus, command.NodeId);
            await ReplyAsync(command.CmdId, "REJECTED", 11,
                $"AMR이 현재 작업 중입니다. (상태: {status.WorkStatus})", ct);
            return;
        }

        // 3. 위치 태그 매핑 조회
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var mapping = await db.LocationTagMappings
            .FirstOrDefaultAsync(m => m.LocationTag == command.NodeId, ct);

        if (mapping == null)
        {
            _logger.LogWarning("위치 태그 매핑을 찾을 수 없음: NodeId={NodeId}", command.NodeId);
            await ReplyAsync(command.CmdId, "REJECTED", 20,
                $"등록되지 않은 위치 태그입니다: {command.NodeId}", ct);
            return;
        }

        // 4. Task/Job Index 설정 후 실행
        _logger.LogInformation("이동 명령 실행: NodeId={NodeId} → TaskIndex={TaskIndex}, JobIndex={JobIndex}",
            command.NodeId, mapping.TaskIndex, mapping.JobIndex);

        await _amrService.SetTaskIndexAsync((ushort)mapping.TaskIndex, ct);
        await _amrService.SetJobIndexAsync((ushort)mapping.JobIndex, ct);
        await _amrService.SetExecutionControlAsync(ExecutionControl.Start, ct);

        // 5. 정상 응답
        await ReplyAsync(command.CmdId, "ACCEPTED", 0,
            $"이동 명령 수락: {command.NodeId} (Task={mapping.TaskIndex}, Job={mapping.JobIndex})", ct);
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
