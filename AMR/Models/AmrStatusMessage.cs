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

    /// <summary>RobotStatus에서 MQTT 전송용 DTO로 변환</summary>
    public static AmrStatusMessage FromRobotStatus(RobotStatus status, Alarm? alarm = null) => new()
    {
        State = new AmrStateInfo
        {
            RunState = status.RobotState == RobotState.Started ? RunState.Run : RunState.Stop,
            FullState = FullState.Empty,
            WorkState = status.WorkStatus,
            VehicleDestNode = string.Empty
        },
        Pose = status.Pose,
        Error = new ErrorInfo
        {
            Code = status.ErrorCode,
            Message = string.Empty,
            AlarmCode = alarm?.Code ?? string.Empty,
            AlarmName = alarm?.Name ?? string.Empty
        },
        Battery = status.Battery,
        Abnormal = null
    };
}
