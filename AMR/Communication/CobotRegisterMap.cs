namespace AMR.Communication;

/// <summary>
/// Cobot Modbus TCP 레지스터 주소 맵
/// 매뉴얼 주소는 1-based, NModbus는 0-based이므로 모든 주소에서 -1 적용.
/// </summary>
public static class CobotRegisterMap
{
    /// <summary>
    /// Coil (FC 0x01/0x05/0x15) — PLC→Robot 비트 쓰기
    /// </summary>
    public static class Coil
    {
        /// <summary>DI0~DI127 비트 입력 시작 주소 (매뉴얼 100)</summary>
        public const ushort DigitalInputStart = 99;

        /// <summary>DI0~DI127 비트 입력 끝 주소 (매뉴얼 227)</summary>
        public const ushort DigitalInputEnd = 226;

        /// <summary>DI 비트 수</summary>
        public const ushort DigitalInputCount = 128;

        /// <summary>제어기 DO 시작 주소 (매뉴얼 300)</summary>
        public const ushort ControllerDoStart = 299;

        /// <summary>제어기 DO 끝 주소 (매뉴얼 317)</summary>
        public const ushort ControllerDoEnd = 316;

        /// <summary>일시정지 (매뉴얼 500)</summary>
        public const ushort Pause = 499;

        /// <summary>복구 (매뉴얼 501)</summary>
        public const ushort Recovery = 500;

        /// <summary>시작 (매뉴얼 502)</summary>
        public const ushort Start = 501;

        /// <summary>정지 (매뉴얼 503)</summary>
        public const ushort Stop = 502;

        /// <summary>원점 이동 (매뉴얼 504)</summary>
        public const ushort MoveToJobOrigin = 503;

        /// <summary>수동/자동 전환 (매뉴얼 505)</summary>
        public const ushort ManualAutoSwitch = 504;

        /// <summary>메인 프로그램 시작 (매뉴얼 506)</summary>
        public const ushort StartMainProgram = 505;

        /// <summary>전체 오류 해제 (매뉴얼 510)</summary>
        public const ushort ClearAllFaults = 509;
    }

    /// <summary>
    /// Discrete Input (FC 0x02) — Robot→PLC 비트 읽기
    /// </summary>
    public static class DiscreteInput
    {
        /// <summary>DO0~DO127 비트 출력 시작 주소 (매뉴얼 100)</summary>
        public const ushort DigitalOutputStart = 99;

        /// <summary>DO0~DO127 비트 출력 끝 주소 (매뉴얼 227)</summary>
        public const ushort DigitalOutputEnd = 226;

        /// <summary>DO 비트 수</summary>
        public const ushort DigitalOutputCount = 128;
    }

    /// <summary>
    /// Holding Register (FC 0x03/0x06/0x16) — PLC→Robot 워드 쓰기
    /// </summary>
    public static class Holding
    {
        /// <summary>AI0~AI31 아날로그 입력 시작 주소 (매뉴얼 100)</summary>
        public const ushort AnalogInputStart = 99;

        /// <summary>AI0~AI31 아날로그 입력 끝 주소 (매뉴얼 131)</summary>
        public const ushort AnalogInputEnd = 130;

        /// <summary>AI 워드 수</summary>
        public const ushort AnalogInputCount = 32;
    }

    /// <summary>
    /// Input Register (FC 0x04) — Robot→PLC 워드 읽기
    /// </summary>
    public static class Input
    {
        /// <summary>AO0~AO31 아날로그 출력 시작 주소 (매뉴얼 100)</summary>
        public const ushort AnalogOutputStart = 99;

        /// <summary>AO0~AO31 아날로그 출력 끝 주소 (매뉴얼 131)</summary>
        public const ushort AnalogOutputEnd = 130;

        /// <summary>AO 워드 수</summary>
        public const ushort AnalogOutputCount = 32;

        /// <summary>Enable 상태 (0=Not enabled, 1=Enabled) (매뉴얼 310)</summary>
        public const ushort EnableState = 309;

        /// <summary>로봇 모드 (1=Manual, 0=Automatic) (매뉴얼 311)</summary>
        public const ushort RobotMode = 310;

        /// <summary>동작 상태 (1=Stop, 2=Run, 3=Pause, 4=Drag) (매뉴얼 312)</summary>
        public const ushort OperationStatus = 311;

        /// <summary>Tool 번호 (매뉴얼 313)</summary>
        public const ushort ToolNo = 312;

        /// <summary>Job 번호 (매뉴얼 314)</summary>
        public const ushort JobNumber = 313;

        /// <summary>비상정지 상태 (0=정상, 1=비상정지) (매뉴얼 315)</summary>
        public const ushort ScrumState = 314;

        /// <summary>로봇 상태 — Super soft limit fault (매뉴얼 316)</summary>
        public const ushort RobotStatusFault = 315;

        /// <summary>Master fault code (매뉴얼 317)</summary>
        public const ushort MasterFaultCode = 316;

        /// <summary>Sub fault code (매뉴얼 318)</summary>
        public const ushort SubFaultCode = 317;

        /// <summary>충돌 감지 (1=Collision, 0=No collision) (매뉴얼 319)</summary>
        public const ushort CollisionDetection = 318;

        /// <summary>Motion in place signal (매뉴얼 320)</summary>
        public const ushort MotionInPlace = 319;

        /// <summary>Safety stop signal S0 (매뉴얼 321)</summary>
        public const ushort SafetyStopS0 = 320;

        /// <summary>Safety stop signal S1 (매뉴얼 322)</summary>
        public const ushort SafetyStopS1 = 321;

        /// <summary>상태 레지스터 시작 주소 (매뉴얼 310)</summary>
        public const ushort StatusStart = 309;

        /// <summary>상태 레지스터 수 (매뉴얼 310~322 = 13개)</summary>
        public const ushort StatusCount = 13;
    }
}
