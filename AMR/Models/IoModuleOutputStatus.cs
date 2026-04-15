namespace AMR.Models;

/// <summary>
/// LS XEL-BSSRT I/O 모듈 출력 상태 (Coil Y000~Y005)
/// </summary>
public class IoModuleOutputStatus
{
    /// <summary>Y000 — Tower Lamp Red</summary>
    public bool TowerLampRed { get; set; }

    /// <summary>Y001 — Tower Lamp Yellow</summary>
    public bool TowerLampYellow { get; set; }

    /// <summary>Y002 — Tower Lamp Green</summary>
    public bool TowerLampGreen { get; set; }

    /// <summary>Y003 — Tower Lamp Buzzer</summary>
    public bool TowerLampBuzzer { get; set; }

    /// <summary>Y004 — Reset Switch Lamp</summary>
    public bool ResetSwLamp { get; set; }

    /// <summary>Y005 — Cobot Servo ON/OFF</summary>
    public bool CobotServoOnOff { get; set; }
}
