using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMR.Models;
using MQTTnet;
using MQTTnet.Client;

namespace AMR.Communication;

/// <summary>
/// MQTT 브로커 통신 클라이언트 (상태 퍼블리시 + 명령 구독)
/// </summary>
public class AmrMqttClient : IDisposable
{
    private readonly MqttClientSettings _settings;
    private readonly IMqttClient _mqttClient;
    private MqttClientOptions? _mqttOptions;
    private bool _disposed;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>MQTT 브로커 연결 상태</summary>
    public bool IsConnected => _mqttClient.IsConnected;

    /// <summary>명령 수신 이벤트 (ACS → AMR)</summary>
    public event Action<AmrCommand>? OnCommandReceived;

    public AmrMqttClient(MqttClientSettings settings)
    {
        _settings = settings;
        _mqttClient = new MqttFactory().CreateMqttClient();

        _mqttClient.ApplicationMessageReceivedAsync += HandleMessageAsync;
    }

    /// <summary>MQTT 브로커에 연결하고 command 토픽을 구독한다.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        // 이전 연결이 남아 있으면 정리
        if (_mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync();
        }

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.BrokerAddress, _settings.BrokerPort)
            .WithClientId(_settings.ClientId)
            .WithCleanSession();

        if (!string.IsNullOrEmpty(_settings.Username))
        {
            optionsBuilder.WithCredentials(_settings.Username, _settings.Password);
        }

        _mqttOptions = optionsBuilder.Build();

        await _mqttClient.ConnectAsync(_mqttOptions, ct);
        await SubscribeCommandAsync(ct);
    }

    /// <summary>MQTT 연결을 해제한다.</summary>
    public async Task StopAsync()
    {
        if (_mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync();
        }
    }

    /// <summary>로봇 상태를 MQTT로 퍼블리시한다.</summary>
    public async Task PublishStatusAsync(AmrStatusMessage statusMessage, CancellationToken ct = default)
    {
        if (!_mqttClient.IsConnected)
            return;

        var payload = JsonSerializer.Serialize(statusMessage, _jsonOptions);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"amr/{_settings.ClientId}/status")
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag()
            .Build();

        await _mqttClient.PublishAsync(message, ct);
    }

    /// <summary>Heartbeat 메시지를 MQTT로 퍼블리시한다.</summary>
    public async Task PublishHeartbeatAsync(CancellationToken ct = default)
    {
        if (!_mqttClient.IsConnected)
            return;

        var payload = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now.ToString("o") }, _jsonOptions);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"amr/{_settings.ClientId}/heartbeat")
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
            .Build();

        await _mqttClient.PublishAsync(message, ct);
    }

    /// <summary>명령 응답을 MQTT로 퍼블리시한다.</summary>
    public async Task PublishReplyAsync(CommandReply reply, CancellationToken ct = default)
    {
        if (!_mqttClient.IsConnected)
            return;

        var payload = JsonSerializer.Serialize(reply, _jsonOptions);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"amr/{_settings.ClientId}/reply")
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _mqttClient.PublishAsync(message, ct);
    }

    private async Task SubscribeCommandAsync(CancellationToken ct = default)
    {
        var topicFilter = new MqttTopicFilterBuilder()
            .WithTopic($"amr/{_settings.ClientId}/command")
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _mqttClient.SubscribeAsync(topicFilter, ct);
    }

    private Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var expectedTopic = $"amr/{_settings.ClientId}/command";

        if (topic != expectedTopic)
            return Task.CompletedTask;

        try
        {
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            var command = JsonSerializer.Deserialize<AmrCommand>(payload, _jsonOptions);

            if (command is not null && !string.IsNullOrEmpty(command.Command))
            {
                OnCommandReceived?.Invoke(command);
            }
        }
        catch
        {
            // 잘못된 JSON 페이로드 무시
        }

        return Task.CompletedTask;
    }

    #region Dispose

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _mqttClient.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}
