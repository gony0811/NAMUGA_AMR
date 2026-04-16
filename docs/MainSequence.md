# AMR 운영 Main Sequence

## 1. AMR Magazine Pickup Sequence

(1) Mqtt MoveCmd Received from ACS (Ei) 
    - port 정보가 LEFT or RIGHT가 아닌 경우, port 정보는 무시하고 NodeId 정보로 이동 명령 처리
    - jobType은 거억했다 해당 작업을 수행해야함.

```json
{
  "cmdId": "20260325_160501_001",
  "command": "moveCmd",
  "nodeId": "N0001",
  "port": "LEFT",
  "jobType": "LOAD"
}
```

(2) Mqtt MoveCmdReply Send to ACS (Ei)
(3) Ei의 MoveCmd 정보에서 NodeId 정보 추출하여 AmrService의 Task Index로 변환하여 AMR 이동 명령 전송
(4) 도착 후, Ei 서버로 Arrived 메시지 전송 또는 VehicleStatus 정보를 이용해서 ACS에서 도착 완료 처리 할 수도 있음. 
    - 상세 메시지 사양은 추후 정의 예정 (임의 정의 요청)
    - NodeId 정보로 도착 인지하는 경우는 메시지는 사용하지 않을 예정

(5) ACS에서 ActionCmd를 받는 경우, 작업 정보 (LOAD, UNLOAD), Port정보 (LEFT, RIGHT) 등을 포함하여 명령을 전송할 예정   
    - ActionCmd는 MoveCmd와 별도로 처리할 예정
    - 

(5) MoveCmd로 부터 추출한 port정보가 LEFT or RIGHT가 아닌경우 ActionCmd 대기 없이 바로 다음 시퀀스 진행
    

(6) Cobot QR Code Reading Position 이동 
    - Position 이동 명령 인터페이스는 docs/cobot_interface.md파일 참고
    
(7) CameraService에서 QR Code 인식 후, 화면 중심에서 인식된 QR 코드의 x, y, theta 거리 offset을 CobotService의 통신을 이용해 Cobot으로 전달 (Modbus AI 0,1,2 레지스터 이용)
    - CobotService에서 Cobot으로 위치 보정 명령 전송

----------------------------------------------------------------------
-- [3] 위치 오프셋 변수 (Modbus AI에서 수신)
----------------------------------------------------------------------
--AI0: dx mm
--AI1: dy mm
--AI2: dTheta degree

(8) movecmd에서 받은 port정보(LEFT/RIGHT) 위치로 이동하여 PICKUP 작업 수행    
    - Position 이동 명령 인터페이스는 docs/cobot_interface.md파일 참고
    - 작업 수행은 CobotService에서 Cobot으로 명령 전송 (Modbus DO 이용)

(9) AMR의 1번 Port에 PLACE 작업 수행
    - Position 이동 명령 인터페이스는 docs/cobot_interface.md파일 참고
    - 작업 수행은 CobotService에서 Cobot으로 명령 전송 (Modbus DO 이용)
    - 금번 시퀀스에서는 1번 Port만 사용 예정

(10) Pickup 완료 후, Ei 서버로 LoadingCompleted 메시지 전송 또는 VehicleStatus 정보를 이용해서 ACS에서 Pickup 완료 처리 할 수도 있음. 
    - 상세 메시지 사양은 추후 정의 예정 (임의 정의 요청)
    - NodeId 정보로 도착 인지하는 경우는 메시지는 사용하지 않을 예정

(8) AMR이 다음 이동 명령을 기다리는 상태로 전환 (WorkState: Idle)
