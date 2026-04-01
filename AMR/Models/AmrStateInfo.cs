using AMR.Enums;

namespace AMR.Models;

/// <summary>
/// MQTT Status용 로봇 동작 상태 (state 객체)
/// </summary>
public record AmrStateInfo
{
    /// <summary>로봇 동작 상태 (Run/Stop)</summary>
    public RunState RunState { get; init; }

    /// <summary>적재 상태 (Full/Empty)</summary>
    public FullState FullState { get; init; }

    /// <summary>작업 상태 (Idle/Moving/Docking/Jog)</summary>
    public WorkStatus WorkState { get; init; }

    /// <summary>현재 설정된 목적지 노드</summary>
    public string VehicleDestNode { get; init; } = string.Empty;
}
