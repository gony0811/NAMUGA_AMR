namespace AMR.Models;

/// <summary>차량 알람 정의 (docs/vehicle_alarm.md)</summary>
public record Alarm(string Id, int Code, string Name)
{
    public static readonly Alarm CobotNotReady = new("ERR-100", 100, "Cobot Not Ready");
    public static readonly Alarm AmrNotReady = new("ERR-101", 101, "AMR Not Ready");
    public static readonly Alarm CobotCollision = new("ERR-103", 103, "Cobot Collision Error");
    public static readonly Alarm AmrMapMatchingError = new("ERR-104", 104, "AMR Map Matching Error");
    public static readonly Alarm AmrSlotsFull = new("ERR-110", 110, "AMR All Slots Occupied");
    public static readonly Alarm AmrSlotEmpty = new("ERR-111", 111, "AMR Source Slot Empty");
    public static readonly Alarm MaterialPortEmpty = new("ERR-112", 112, "Material Port All Slots Empty");
    public static readonly Alarm MaterialPortFull = new("ERR-113", 113, "Material Port All Slots Occupied");

    // EXCHANGE 시나리오 신규 (docs/ACS-AMR_mqtt_exchangecmd.md 8.2)
    public static readonly Alarm PickupSourceMagazineNotFound = new("ERR-114", 114, "Pickup Source Magazine Not Found");
    public static readonly Alarm ExchangeSlotStateMismatch = new("ERR-115", 115, "Exchange Slot State Mismatch");
    public static readonly Alarm ActionCmdWaitTimeout = new("ERR-116", 116, "ActionCmd Wait Timeout");
}
