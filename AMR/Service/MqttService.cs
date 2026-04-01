using AMR.Communication;
using AMR.Models;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// MQTT 통신 서비스 — ACS와의 상태 퍼블리시, 명령 수신, 응답 함수 집합
/// </summary>
public class MqttService
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

    #region 연결 관리

    /// <summary>MQTT 브로커에 연결하고 command 토픽을 구독한다.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _mqttClient.StartAsync(ct);
        _logger.LogInformation("MQTT 브로커 연결 완료");
    }

    /// <summary>MQTT 연결을 해제한다.</summary>
    public async Task DisconnectAsync()
    {
        await _mqttClient.StopAsync();
        _logger.LogInformation("MQTT 브로커 연결 해제");
    }

    #endregion

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
