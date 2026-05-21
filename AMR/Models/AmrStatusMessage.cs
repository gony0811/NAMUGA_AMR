using System.Text.Json.Serialization;
using AMR.Enums;

namespace AMR.Models;

/// <summary>
/// MQTT 전송용 로봇 상태 DTO (amr/{ClientId}/status)
/// </summary>
public record AmrStatusMessage
{
    /// <summary>로봇 동작 상태</summary>
    public AmrStateInfo State { get; init; } = new();

    /// <summary>로봇 현재 위치</summary>
    public RobotPose Pose { get; init; } = new(0, 0, 0);

    /// <summary>에러 정보</summary>
    public ErrorInfo Error { get; init; } = new();

    /// <summary>배터리 상태</summary>
    public BatteryStatus Battery { get; init; } = new();

    /// <summary>비정상 상황 보고 (없으면 null → JSON에서 생략)</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AbnormalInfo? Abnormal { get; init; }

    /// <summary>배터리 잔량이 이 값 미만이면서 충전중이면 RunState=Charge</summary>
    private const float LowBatteryPercent = 20f;

    /// <summary>RobotStatus에서 MQTT 전송용 DTO로 변환</summary>
    public static AmrStatusMessage FromRobotStatus(RobotStatus status, Alarm? alarm = null, AbnormalInfo? abnormal = null)
    {
        // 배터리 < 20% AND 충전중 → Charge
        // 그 외엔 RobotState 기준 Run/Stop (≥20% 이면 Stop = Idle 로 새 명령 수용)
        RunState runState;
        if (status.Battery.LevelPercent < LowBatteryPercent
            && status.Battery.ChargingState == ChargingState.Charging)
        {
            runState = RunState.Charge;
        }
        else if (status.RobotState == RobotState.Started)
        {
            runState = RunState.Run;
        }
        else
        {
            runState = RunState.Stop;
        }

        return new AmrStatusMessage
        {
            State = new AmrStateInfo
            {
                RunState = runState,
                FullState = FullState.Empty,
                WorkState = status.WorkStatus,
                VehicleDestNode = string.Empty
            },
            Pose = status.Pose,
            Error = new ErrorInfo
            {
                Code = alarm?.Code ?? 0,
                Name = alarm?.Name ?? string.Empty
            },
            Battery = status.Battery,
            Abnormal = abnormal
        };
    }
}
