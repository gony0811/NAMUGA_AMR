namespace AMR.Communication;

/// <summary>
/// LS산전 XEL-BSSRT I/O 모듈 Modbus 주소 맵 (0-based PDU 주소).
/// docs/IO_LIST.HEIC 기준 X000~X005 = Discrete Input 0~5, Y000~Y005 = Coil 0~5.
/// </summary>
public static class IoModuleRegisterMap
{
    /// <summary>
    /// Discrete Input (FC 0x02) — 센서/스위치 입력 비트
    /// </summary>
    public static class DiscreteInput
    {
        /// <summary>X000 — EMO (비상정지 스위치)</summary>
        public const ushort Emo = 0;

        /// <summary>X001 — RESET 스위치</summary>
        public const ushort Reset = 1;

        /// <summary>X002 — MZ DETECT 1</summary>
        public const ushort MzDetect1 = 2;

        /// <summary>X003 — MZ DETECT 2</summary>
        public const ushort MzDetect2 = 3;

        /// <summary>X004 — MZ DETECT 3</summary>
        public const ushort MzDetect3 = 4;

        /// <summary>X005 — MZ DETECT 4</summary>
        public const ushort MzDetect4 = 5;

        /// <summary>입력 시작 주소 (X000)</summary>
        public const ushort InputStart = 0;

        /// <summary>입력 비트 수 (X000~X005)</summary>
        public const ushort InputCount = 6;
    }

    /// <summary>
    /// Coil (FC 0x01/0x05/0x0F) — 램프/부저/서보 출력 비트
    /// </summary>
    public static class Coil
    {
        /// <summary>Y000 — Tower Lamp Red</summary>
        public const ushort TowerLampRed = 0;

        /// <summary>Y001 — Tower Lamp Yellow</summary>
        public const ushort TowerLampYellow = 1;

        /// <summary>Y002 — Tower Lamp Green</summary>
        public const ushort TowerLampGreen = 2;

        /// <summary>Y003 — Tower Lamp Buzzer</summary>
        public const ushort TowerLampBuzzer = 3;

        /// <summary>Y004 — Reset Switch Lamp</summary>
        public const ushort ResetSwLamp = 4;

        /// <summary>Y005 — Cobot Servo ON/OFF</summary>
        public const ushort CobotServoOnOff = 5;

        /// <summary>출력 시작 주소 (Y000)</summary>
        public const ushort OutputStart = 0;

        /// <summary>출력 비트 수 (Y000~Y005)</summary>
        public const ushort OutputCount = 6;
    }
}
