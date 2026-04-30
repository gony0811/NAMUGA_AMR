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

    /// <summary>마지막으로 도착 확인된 노드 ID — 동일 위치 명령 시 이동 스킵 판단용</summary>
    public string? CurrentNodeId { get; set; }

    /// <summary>
    /// 마지막 도착 시점의 AMR 실제 pose (스킵 결정 직전에 현재 pose와 비교하여
    /// 사람이 AMR을 옮겼는지 감지하는 데 사용)
    /// </summary>
    public RobotPose? LastArrivedPose { get; set; }

    /// <summary>포트 (LEFT/RIGHT)</summary>
    public string? Port { get; set; }

    /// <summary>작업 유형 (LOAD/UNLOAD)</summary>
    public string? JobType { get; set; }

    /// <summary>포트 유형 (FACILITY/MATERIAL)</summary>
    public string? PortType { get; set; }

    /// <summary>AMR 슬롯 번호 (1~4)</summary>
    public int AmrSlot { get; set; } = 1;

    /// <summary>에러 메시지 (Faulted 상태일 때)</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>시퀀스 시작 시각</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>현재 단계 시작 시각</summary>
    public DateTime? StepStartedAt { get; set; }

    /// <summary>데모 모드 실행 중 여부</summary>
    public bool IsDemoRunning { get; set; }

    /// <summary>데모 현재 사이클 번호 (1부터 시작)</summary>
    public int DemoCycle { get; set; }

    /// <summary>데모 사이클 내 현재 시퀀스 인덱스 (0~3)</summary>
    public int DemoStepIndex { get; set; }
}
