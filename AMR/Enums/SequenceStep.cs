namespace AMR.Enums;

/// <summary>
/// Move Command 시퀀스 단계
/// </summary>
public enum SequenceStep
{
    /// <summary>시퀀스 미실행 (대기)</summary>
    Idle = 0,

    /// <summary>Step 1: moveCmd 수신, jobType/port 저장</summary>
    MoveCmdReceived = 1,

    /// <summary>Step 2: ACS에 ACCEPTED 응답</summary>
    MoveCmdReply = 2,

    /// <summary>Step 3: NodeId → TaskIndex/JobIndex 변환, AMR 이동 명령</summary>
    SendMoveCommand = 3,

    /// <summary>Step 4: AMR 도착 대기 (WorkStatus polling)</summary>
    WaitArrival = 4,

    /// <summary>Step 5: port가 LEFT/RIGHT면 ActionCmd 대기, 아니면 스킵</summary>
    WaitActionCmd = 5,

    /// <summary>Step 6: Cobot을 QR 읽기 위치로 이동</summary>
    CobotQrPosition = 6,

    /// <summary>Step 7: Camera QR 인식 → offset을 Cobot AI에 전달</summary>
    CameraQrRead = 7,

    /// <summary>Step 8: port 위치에서 PICKUP 수행</summary>
    CobotPickup = 8,

    /// <summary>Step 9: AMR Port 1에 PLACE 수행</summary>
    CobotPlace = 9,

    /// <summary>Step 10: 완료 통보, Idle 복귀</summary>
    Complete = 10,

    /// <summary>에러 상태</summary>
    Faulted = 99
}
