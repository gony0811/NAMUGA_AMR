using System.Collections.Concurrent;
using AMR.Communication;
using AMR.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// MQTT 통신 서비스 — 자동 연결/재연결 + Heartbeat + ACS와의 상태 퍼블리시, 명령 수신, 응답
/// </summary>
public class MqttService : BackgroundService
{
    private readonly AmrMqttClient _mqttClient;
    private readonly ILogger<MqttService> _logger;

    // 큐 내부적으로 enqueue 시점을 함께 보관 — TTL 만료 / 상한 초과 시 자동 제거.
    // 외부 API(TryDequeueActionCmd) 는 AmrCommand 만 노출해서 consumer 변경 불필요.
    private readonly ConcurrentQueue<(DateTime EnqueuedAt, AmrCommand Command)> _actionCmdQueue = new();

    /// <summary>ActionCmd 큐 상한 — 초과 시 가장 오래된 것부터 제거</summary>
    private const int MaxActionCmdQueue = 100;

    /// <summary>ActionCmd TTL — 시퀀스가 소비하지 않고 이 시간 지나면 자동 폐기</summary>
    private static readonly TimeSpan ActionCmdTtl = TimeSpan.FromSeconds(30);

    /// <summary>명령 수신 이벤트 (ACS → AMR)</summary>
    public event Action<AmrCommand>? OnCommandReceived;

    public MqttService(AmrMqttClient mqttClient, ILogger<MqttService> logger)
    {
        _mqttClient = mqttClient;
        _logger = logger;

        _mqttClient.OnCommandReceived += command =>
        {
            _logger.LogInformation("MQTT 명령 수신: {Command} (cmdId: {CmdId})", command.Command, command.CmdId);

            if (command.Command == "actionCmd")
            {
                EnqueueActionCmd(command);
            }

            OnCommandReceived?.Invoke(command);
        };
    }

    /// <summary>
    /// 테스트용 — Web UI 에서 ActionCmd 를 수동으로 큐에 주입.
    /// MQTT 를 거치지 않고 시뮬레이션 가능 (ACS 없이 설비포트 시퀀스 테스트).
    /// </summary>
    public void InjectActionCmdForTest(AmrCommand command)
    {
        _logger.LogInformation("ActionCmd 수동 주입(테스트): CmdId={CmdId}, Port={Port}, AmrSlot={Slot}",
            command.CmdId, command.Port ?? "없음", command.AmrSlot);
        EnqueueActionCmd(command);
        OnCommandReceived?.Invoke(command);
    }

    /// <summary>
    /// ActionCmd 를 큐에 추가하면서 동시에 만료/초과 항목 제거.
    /// 시퀀스가 소비하지 않는 명령들이 영원히 쌓이는 메모리 누수 방지.
    /// </summary>
    private void EnqueueActionCmd(AmrCommand command)
    {
        var now = DateTime.UtcNow;
        _actionCmdQueue.Enqueue((now, command));
        _logger.LogInformation("ActionCmd 큐에 추가: CmdId={CmdId}", command.CmdId);

        // 만료(TTL 초과) 항목 제거 — 큐 앞쪽부터 검사
        while (_actionCmdQueue.TryPeek(out var head) && (now - head.EnqueuedAt) > ActionCmdTtl)
        {
            if (_actionCmdQueue.TryDequeue(out var dropped))
            {
                _logger.LogWarning(
                    "ActionCmd 만료로 폐기 — TTL {Ttl}s 초과: CmdId={CmdId}",
                    ActionCmdTtl.TotalSeconds, dropped.Command.CmdId);
            }
        }

        // 상한 초과 시 가장 오래된 항목 제거
        while (_actionCmdQueue.Count > MaxActionCmdQueue)
        {
            if (_actionCmdQueue.TryDequeue(out var dropped))
            {
                _logger.LogWarning(
                    "ActionCmd 큐 상한({Max}) 초과 — 가장 오래된 항목 폐기: CmdId={CmdId}",
                    MaxActionCmdQueue, dropped.Command.CmdId);
            }
        }
    }

    /// <summary>ActionCmd 큐에서 명령을 꺼낸다. TTL 만료된 항목은 자동 스킵.</summary>
    public bool TryDequeueActionCmd(out AmrCommand command)
    {
        var now = DateTime.UtcNow;

        // TTL 만료된 head 항목들을 건너뛰면서 첫 유효 항목 반환
        while (_actionCmdQueue.TryDequeue(out var tuple))
        {
            if ((now - tuple.EnqueuedAt) <= ActionCmdTtl)
            {
                command = tuple.Command;
                return true;
            }

            _logger.LogWarning(
                "ActionCmd dequeue 시점에 만료 감지 — 폐기: CmdId={CmdId}",
                tuple.Command.CmdId);
        }

        command = null!;
        return false;
    }

    /// <summary>MQTT 브로커 연결 상태</summary>
    public bool IsConnected => _mqttClient.IsConnected;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MqttService 시작");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!IsConnected)
                {
                    _logger.LogWarning("MQTT 브로커 연결 시도");
                    await _mqttClient.StartAsync(stoppingToken);
                    _logger.LogInformation("MQTT 브로커 연결 완료");
                }

                // Heartbeat 퍼블리시 (1초 간격)
                if (IsConnected)
                {
                    await _mqttClient.PublishHeartbeatAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT 서비스 오류 — 5초 후 재시도");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _mqttClient.StopAsync();
        _logger.LogInformation("MqttService 종료");
        await base.StopAsync(cancellationToken);
    }

    #region 퍼블리시

    /// <summary>로봇 상태를 퍼블리시한다.</summary>
    public async Task PublishStatusAsync(AmrStatusMessage statusMessage, CancellationToken ct = default)
    {
        await _mqttClient.PublishStatusAsync(statusMessage, ct);
    }

    /// <summary>명령 응답을 퍼블리시한다.</summary>
    public async Task PublishReplyAsync(CommandReply reply, CancellationToken ct = default)
    {
        await _mqttClient.PublishReplyAsync(reply, ct);
        _logger.LogInformation("MQTT Reply 발행: {CmdId} → {Status}", reply.CmdId, reply.Status);
    }

    #endregion
}
