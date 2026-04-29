namespace AMR.Models;

/// <summary>
/// AMR → ACS 명령 응답 (amr/{ClientId}/reply)
/// </summary>
public class CommandReply
{
    /// <summary>원본 명령 ID</summary>
    public string CmdId { get; set; } = string.Empty;

    /// <summary>상태 (ACCEPTED, REJECTED, EXECUTING, COMPLETED, FAILED)</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>결과 코드 (0: 성공, 기타: 에러 코드)</summary>
    public int ResultCode { get; set; }

    /// <summary>상세 메시지</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>작업 유형 (LOAD, UNLOAD, EXCHANGE)</summary>
    public string? JobType { get; set; }

    /// <summary>타임스탬프 (ISO 8601)</summary>
    public string Timestamp { get; set; } = string.Empty;
}
