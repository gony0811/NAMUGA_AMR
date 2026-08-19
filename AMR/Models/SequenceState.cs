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

    /// <summary>마지막으로 도착 확인된 노드 ID (로그/UI 표시용)</summary>
    public string? CurrentNodeId { get; set; }

    /// <summary>포트 (LEFT/RIGHT)</summary>
    public string? Port { get; set; }

    /// <summary>작업 유형 (LOAD/UNLOAD)</summary>
    public string? JobType { get; set; }

    /// <summary>포트 유형 — "EQP" 포함 시 설비포트, 그 외는 자재포트</summary>
    public string? PortType { get; set; }

    /// <summary>AMR 슬롯 번호 (1~4)</summary>
    public int AmrSlot { get; set; } = 1;

    /// <summary>에러 메시지 (Faulted 상태일 때)</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>시퀀스 시작 시각</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>현재 단계 시작 시각</summary>
    public DateTime? StepStartedAt { get; set; }

    // ===== EXCHANGE 시퀀스 상태 =====

    /// <summary>교환 시퀀스 실행 중 여부</summary>
    public bool IsExchange { get; set; }

    /// <summary>ACS Exchange Job ID (교환 시퀀스에서만)</summary>
    public string? JobId { get; set; }

    /// <summary>신규 매거진 AMR 슬롯 (1|2)</summary>
    public int LoadSlot { get; set; }

    /// <summary>회수 매거진 AMR 슬롯 (3|4)</summary>
    public int UnloadSlot { get; set; }

    /// <summary>데모 모드 실행 중 여부</summary>
    public bool IsDemoRunning { get; set; }

    /// <summary>데모 현재 사이클 번호 (1부터 시작)</summary>
    public int DemoCycle { get; set; }

    /// <summary>데모 사이클 내 현재 시퀀스 인덱스 (0~3)</summary>
    public int DemoStepIndex { get; set; }
}
