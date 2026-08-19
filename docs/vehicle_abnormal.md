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
### (2026-08-11 초안 — EXCHANGE 시나리오, docs/ACS-AMR_mqtt_exchangecmd.md 참조)
### Condition
1. 매거진 적재 후 취소(JOBCANCEL 판정 C3)로 cancelCmd 를 수신했을 때
   - 시퀀스 중단 → returnNode(기본: 자동충전 노드)로 복귀 완료 후,
     차량 ALARM 상태(경광등 Red + 부저)로 진입하며 본 abnormal 을 status 에 포함하여 보고
   - Node = 복귀 노드 (예: N1001)
### Description
- Exchange job was canceled while magazines are still on board; AMR returned to the hold/charge node and is waiting for operator action.
- 작업자는 탑재된 매거진(신규 슬롯1|2 / 회수 슬롯3|4)을 실물 회수해야 한다.
- 포트 센서 복귀로 자동 해제되지 않는 latched abnormal.
  - 정상 해제: 작업자가 매거진 회수 후 Reset 을 짧게 눌러 운행 복귀시킬 때.
  - Fallback 해제: 새 job(moveCmd/exchangeCmd)이 시작되는 시점.

