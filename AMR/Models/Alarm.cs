namespace AMR.Models;

/// <summary>차량 알람 정의 (docs/vehicle_alarm.md)</summary>
public record Alarm(string Id, int Code, string Name)
{
    public static readonly Alarm CobotNotReady = new("ERR-100", 100, "Cobot Not Ready");
    public static readonly Alarm AmrNotReady = new("ERR-101", 101, "AMR Not Ready");
    public static readonly Alarm CobotCollision = new("ERR-103", 103, "Cobot Collision Error");
    public static readonly Alarm AmrMapMatchingError = new("ERR-104", 104, "AMR Map Matching Error");
}
