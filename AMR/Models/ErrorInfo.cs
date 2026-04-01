namespace AMR.Models;

/// <summary>
/// MQTT Status용 에러 정보 (error 객체)
/// </summary>
public record ErrorInfo
{
    /// <summary>에러 코드 (0 = 정상)</summary>
    public ushort Code { get; init; }

    /// <summary>에러 메시지</summary>
    public string Message { get; init; } = string.Empty;
}
