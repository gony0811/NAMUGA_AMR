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

    /// <summary>명령 수신 이벤트 (ACS → AMR)</summary>
    public event Action<AmrCommand>? OnCommandReceived;

    public MqttService(AmrMqttClient mqttClient, ILogger<MqttService> logger)
    {
        _mqttClient = mqttClient;
        _logger = logger;

        _mqttClient.OnCommandReceived += command =>
        {
            _logger.LogInformation("MQTT 명령 수신: {Command} (cmdId: {CmdId})", command.Command, command.CmdId);
            OnCommandReceived?.Invoke(command);
        };
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
