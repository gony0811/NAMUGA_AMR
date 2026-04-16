using AMR.Enums;

namespace AMR.Models;

/// <summary>
/// Move Command 시퀀스 현재 상태
/// </summary>
public class SequenceState
{
    /// <summary>현재 실행 중인 단계</summary>
    public SequenceStep CurrentStep { get; set; } = SequenceStep.Idle;

    /// <summary>시퀀스 실행 중 여부</summary>
    public bool IsRunning { get; set; }

    /// <summary>현재 처리 중인 명령 ID</summary>
    public string? CmdId { get; set; }

    /// <summary>목적지 노드 ID</summary>
    public string? NodeId { get; set; }

    /// <summary>포트 (LEFT/RIGHT)</summary>
    public string? Port { get; set; }

    /// <summary>작업 유형 (LOAD/UNLOAD)</summary>
    public string? JobType { get; set; }

    /// <summary>에러 메시지 (Faulted 상태일 때)</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>시퀀스 시작 시각</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>현재 단계 시작 시각</summary>
    public DateTime? StepStartedAt { get; set; }
}
