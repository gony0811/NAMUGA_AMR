using AMR.Enums;

namespace AMR.Models;

/// <summary>
/// 시퀀스 실행 로그 항목
/// </summary>
public record SequenceLogEntry(
    DateTime Timestamp,
    SequenceStep Step,
    string Message,
    bool IsError);
