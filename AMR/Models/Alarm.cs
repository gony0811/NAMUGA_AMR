namespace AMR.Models;

/// <summary>차량 알람 정의 (docs/vehicle_alarm.md)</summary>
public record Alarm(string Code, string Name)
{
    public static readonly Alarm CobotNotReady = new("ERR-100", "Cobot Not Ready");
}
