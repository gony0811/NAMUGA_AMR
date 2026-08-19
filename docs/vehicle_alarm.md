# AMR Alarm List

## ERR-100
### Cobot Not Ready
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. Modbus Disconnect
2. Cobot Disable
3. Main Program Stop
4. Manual Mode
### Description
- Cobot is not ready to perform a transport task

## ERR-101
### AMR Not Ready
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. Modbus Disconnect
### Description
- AMR is not ready to perform a transport task

## ERR-102
### Camera Doesn’t Ready
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. Camera Disconnect
### Description
- Camera is not ready.

## ERR-103
### Cobot Collision Error
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. Cobot Collision Detected
### Description
- Cobot has detected a collision. Please check the surroundings of the AMR and clear any obstacles.

## ERR-104
### AMR Map Matching Error
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. Map Matching Rate < 30%
### Description
- AMR is having difficulty matching its current position to the map. Please check the environment and ensure that the AMR can see enough landmarks for localization.

## ERR-105
### AMR Magazine Unloaded by manually
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. AMR Processing State is Run
2. AMR Transfer State is TRANSFERING_DEST
3. the trigger is when AMR Port Sensor On -> Off
4. 1, 2, 3 Condition is all true

### Description
- AMR is having difficulty matching its current position to the map. Please check the environment and ensure that the AMR can see enough landmarks for localization.

## ERR-110
### AMR All Slots Occupied
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. UNLOAD(회수) PLACE 시 AMR 슬롯 1~4 전부 점유 (MzDetect 센서 기준)
### Description
- No empty AMR slot to place the magazine. Sequence aborts.

## ERR-111
### AMR Source Slot Empty
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. LOAD(투입) PICK 시 지정 AMR 슬롯에 매거진 미검출
### Description
- The designated AMR source slot is empty. Sequence aborts.

## ERR-112
### Material Port All Slots Empty
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. 자재포트 UNLOAD 시 슬롯1/2(DI20/21 확인 위치) 모두 매거진 미검출
### Description
- No magazine found on the material port. Sequence aborts.

## ERR-113
### Material Port All Slots Occupied
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. 자재포트 LOAD 시 슬롯1/2(DI22/23 확인 위치) 모두 점유
### Description
- No empty slot on the material port. Sequence aborts.

---
# EXCHANGE 시나리오 신규 알람 (docs/ACS-AMR_mqtt_exchange_v0.3.docx §6.1)

## ERR-114
### Pickup Source Magazine Not Found
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. 픽업 구간(moveCmd UNLOAD, 자재포트/버퍼)에서 슬롯1/2 모두 매거진 미검출 (깊이감지)
### Description
- No magazine at the exchange pickup source. Job ends immediately with FAILED (resultCode=30, MAGAZINE_NOT_FOUND). No retry by AMR/ACS — MES must re-issue EXCHANGECMD.

## ERR-115
### Exchange Slot State Mismatch
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. actionCmd 실행 중 지정 amrSlot 상태가 기대와 불일치
   - UNLOAD(OLD 취출): 회수슬롯(3|4)이 이미 점유 / LOAD(NEW 투입): 투입슬롯(1|2)이 비어 있음
### Description
- Slot sensor state does not match the assigned exchange slots. Sequence aborts with FAILED (resultCode=31).

## ERR-116
### ActionCmd Wait Timeout
### Severity (Level: Warning | Critical)
- Critical
### Condition
1. 설비포트 actionCmd 대기 시간이 설정 상한 초과 (기본 무제한 — 상한 설정 시에만 발동)
### Description
- Equipment permission (ACTIONCMD relay) did not arrive within the configured limit. Sequence aborts with FAILED (resultCode=32).
