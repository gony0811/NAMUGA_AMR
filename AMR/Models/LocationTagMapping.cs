using System.ComponentModel.DataAnnotations;

namespace AMR.Models;

/// <summary>
/// 위치 태그 → AMR Task/Job 매핑
/// ACS에서 수신한 NodeId를 TaskIndex, JobIndex로 변환하는 데 사용
/// </summary>
public class LocationTagMapping
{
    public int Id { get; set; }

    /// <summary>위치 태그 (예: N0001)</summary>
    [Required]
    [MaxLength(50)]
    public string LocationTag { get; set; } = string.Empty;

    /// <summary>Task 인덱스 (Holding Register 31에 기록)</summary>
    [Range(0, 65535)]
    public int TaskIndex { get; set; }

    /// <summary>Job 인덱스 (Holding Register 32에 기록)</summary>
    [Range(0, 65535)]
    public int JobIndex { get; set; }

    /// <summary>설명 (선택)</summary>
    [MaxLength(200)]
    public string? Description { get; set; }
}
