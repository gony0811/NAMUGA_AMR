# AMR Abnormal Case List
```
## Template
### Alarm Code
### Alarm Name
### Condition
1. Condition 1
2. Condition 2
### Description
- Description of the alarm
```
------------------------------------------------------------------------
### Abnormal Code: 100
### CARRIER_REMOVED
### Condition
1. PORT1, 2, 3, 4 에서 MAGAZINE이 제거 되었을때 
   - AMR의 cobot에 의해 설비/자재 포트로 Loading하는 시퀀스 이외에 port sensor On->Off로 변경되는 경우
### Description
- Cobot is not ready to perform transport task

------------------------------------------------------------------------
### Abnormal Code: 200
### OPERATOR_ABORT
### Condition
1. 운행 중 운전자가 Reset 스위치를 5초 이상 길게 눌렀을 때
   - AMR이 ClearFaults → Stop → Cobot Manual 전환 안전 시퀀스를 수행한 뒤,
     Node="AMR" 로 본 abnormal 을 status 에 포함하여 ACS 로 보고
### Description
- Operator manually intervened during operation; AMR forced to a safe state.
- ACS should delete the job currently assigned to this AMR.
- 포트 센서 복귀로 자동 해제되지 않는 latched abnormal.
  - 정상 해제: 운전자가 Reset 을 짧게 눌러 코봇 복구 시퀀스를 실행하여 Auto 로 복귀시킬 때.
  - Fallback 해제: 위 과정 없이 새 job(moveCmd)이 시작되는 시점.

------------------------------------------------------------------------
### Abnormal Code: 300
### EXCHANGE_CANCEL_HOLD
### (v0.3 — docs/ACS-AMR_mqtt_exchange_v0.3.docx §6.2)
### Condition
1. 매거진 적재 후 취소(JOBCANCEL C3) 로 정지, 작업자 실물 회수 대기 (latched)
   - v0.3: AMR 은 cancelCmd 수신 시 **정지 → Idle** 만 수행하고 복귀 이동은 하지 않는다.
     충전소 복귀는 ACS 가 별도 moveCmd(portType=CHARGE) 로 지시하고, 차량 ALARM 도 ACS 가 설정한다.
   - 본 abnormal 은 ACS 측 차량 ALARM 과 병행하는 AMR 상태 표시 용도 (선택)
### Description
- Exchange job was canceled while magazines are still on board; operator must remove the on-board magazines.
- 해제: 작업자 매거진 회수 후 Reset(짧게). ACS 차량 reset 시 슬롯/ALARM 해소.
