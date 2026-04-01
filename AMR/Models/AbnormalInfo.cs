namespace AMR.Models;

/// <summary>
/// MQTT Status용 비정상 상황 보고 (abnormal 객체)
/// </summary>
public record AbnormalInfo
{
    /// <summary>비정상 유형 (ex: CHARGING_FAIL)</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>관련 노드 ID</summary>
    public string Node { get; init; } = string.Empty;

    /// <summary>발생 시각 (ISO 8601)</summary>
    public string Timestamp { get; init; } = string.Empty;
}
