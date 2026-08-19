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

    // ===== EXCHANGE 시퀀스 단계 (docs/ACS-AMR_mqtt_exchangecmd.md) =====

    /// <summary>EX Step 10: 픽업지에서 신규 매거진 픽업 → AMR 슬롯 1|2 적재</summary>
    ExPickupNew = 21,

    /// <summary>EX Step 20: 설비 노드로 이동</summary>
    ExMoveToEquip = 22,

    /// <summary>EX 게이트1: actionCmd(type=UNLOAD) 취출 허가 대기</summary>
    ExWaitUnloadPermit = 23,

    /// <summary>EX Step 30: 기존 매거진 회수 (설비 → AMR 슬롯 3|4)</summary>
    ExUnloadOld = 24,

    /// <summary>EX 게이트2: actionCmd(type=LOAD) 투입 허가 대기</summary>
    ExWaitLoadPermit = 25,

    /// <summary>EX Step 40: 신규 매거진 투입 (AMR 슬롯 1|2 → 설비)</summary>
    ExLoadNew = 26,

    /// <summary>EX Step 50: 반납지로 이동 후 기존 매거진 하역</summary>
    ExReturnOld = 27,

    /// <summary>EX Step 60: 교환 완료, Idle 복귀</summary>
    ExComplete = 28,

    /// <summary>적재 후 취소(C3) — 복귀 완료, 작업자 조치 대기 (abnormal 300)</summary>
    CancelHold = 98,

    /// <summary>에러 상태</summary>
    Faulted = 99
}
