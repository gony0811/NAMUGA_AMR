namespace AMR.Communication;

/// <summary>
/// Modbus TCP 연결 설정
/// </summary>
public class AmrModbusTcpSettings
{
    public string IpAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5020;
    public byte SlaveId { get; set; } = 1;
}
