namespace AMR.Communication;

/// <summary>
/// MQTT 클라이언트 연결 설정
/// </summary>
public class MqttClientSettings
{
    public string BrokerAddress { get; set; } = "localhost";
    public int BrokerPort { get; set; } = 1883;
    public string ClientId { get; set; } = "AMR001";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int PublishIntervalMs { get; set; } = 1000;
}
