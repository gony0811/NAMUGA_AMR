namespace AMR.Communication;

/// <summary>
/// Cobot Modbus TCP 연결 설정
/// </summary>
public class CobotModbusTcpSettings
{
    public string IpAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public byte SlaveId { get; set; } = 1;
}
