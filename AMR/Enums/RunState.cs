namespace AMR.Enums;

/// <summary>
/// MQTT Status용 로봇 동작 상태 (state.runState)
/// </summary>
public enum RunState : ushort
{
    Stop = 1,    // 정지 / Idle (새 명령 수용 가능)
    Run = 2,    // 이동 중
    Charge = 3   // 배터리 < 20% 이면서 충전 중
}
