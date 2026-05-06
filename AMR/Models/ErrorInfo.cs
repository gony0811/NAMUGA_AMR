namespace AMR.Models;

/// <summary>
/// MQTT Status용 에러 정보 (error 객체)
/// </summary>
public record ErrorInfo
{
    /// <summary>알람 코드 (0 = 정상)</summary>
    public int Code { get; init; }

    /// <summary>알람 이름 (정상일 때 빈 문자열)</summary>
    public string Name { get; init; } = string.Empty;
}
