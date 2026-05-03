namespace AMR.Models;

/// <summary>
/// MQTT Status용 에러 정보 (error 객체)
/// </summary>
public record ErrorInfo
{
    /// <summary>에러 코드 (0 = 정상, 하드웨어 레벨)</summary>
    public ushort Code { get; init; }

    /// <summary>에러 메시지 (하드웨어 레벨)</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>차량 알람 코드 (ex: ERR-100). 알람 없을 때 빈 문자열.</summary>
    public string AlarmCode { get; init; } = string.Empty;

    /// <summary>차량 알람 이름 (ex: Cobot Not Ready). 알람 없을 때 빈 문자열.</summary>
    public string AlarmName { get; init; } = string.Empty;
}
