namespace AMR.Communication;

/// <summary>
/// LS산전 XEL-BSSRT Smart I/O 모듈 Modbus TCP 연결 설정
/// </summary>
public class IoModuleModbusTcpSettings
{
    public string IpAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public byte SlaveId { get; set; } = 1;
}
