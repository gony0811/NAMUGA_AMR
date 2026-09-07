# AMR 슬롯 검사 규칙 v0.3.1 (2026-09-07)

2026-09-07 현장 이슈(일반 반송 `moveCmd LOAD@EQP amrSlot=1` 이 REJECTED 21 "용도 위반") 반영.

## 규칙 요약

| 명령 | 역할 검사 (1|2 투입 / 3|4 회수) | 점유 검사 |
|------|:---:|------|
| moveCmd `jobType=LOAD/UNLOAD` (일반 반송) | **안 함** — 슬롯 1~4 자유 | LOAD(슬롯 PICK)=**OCCUPIED** 필요 / UNLOAD(슬롯 PLACE)=**EMPTY** 필요 → 위반 시 REJECTED 21 |
| moveCmd `jobType=EXCHANGE` (설비 도킹) | 해당 없음 (슬롯 조작 없음) | 해당 없음 |
| actionCmd `jobType=EXCHANGE` | **함** — UNLOAD=3|4, LOAD=1|2 → 위반 REJECTED 21 | UNLOAD=EMPTY / LOAD=OCCUPIED → 위반 REJECTED 21 |
| actionCmd `jobType=LOAD/UNLOAD` (일반, type 없으면 jobType 사용) | 안 함 | 위와 동일 점유 검사만 |

- 거부 메시지 형식: `amrSlot {n} 비어있음 — LOAD(PLACE)는 점유 슬롯 필요` / `amrSlot {n} 점유 중 — UNLOAD(PICK)는 빈 슬롯 필요`
- resultCode 체계 불변: 21=수락 단계 슬롯 상태/범위, 31=시퀀스 중 슬롯 불일치(ERR-115), 32=EQP ActionCmd 대기 초과(ERR-116)
- 점유 판정 소스: 시뮬레이션 모드=가상 슬롯, 실기=I/O 모듈 MzDetect1~4. 판정 불가(센서 미수신) 시 수락 단계 검사는 생략(시퀀스 중 검증이 방어)
- 거부 시 AMR 은 현 위치 정지·Idle 유지 — 자체 충전소 이동 없음. (기존 "거부 직후 충전소 이동"으로 관찰된 동작은 유휴 자동충전(AutoCharge) 기능이며, v0.3.1 부터 **코드 기본값 off**. appsettings `AutoChargeSettings:Enabled` 또는 /AutoCharge 화면에서 명시적으로 켠 경우에만 동작)

## 설정: GeneralMoveEqpDirectExecute

- appsettings.json:
```json
"SequenceSettings": { "GeneralMoveEqpDirectExecute": false }
```
- **기본 false** = 일반 LOAD/UNLOAD 가 설비(EQP) 도킹 후 **ActionCmd 수신까지 대기(≤120초)** — 기존 설계 (docs/ACS-AMR_mqtt_movecmd.md)
- true = 도킹 직후 ActionCmd 없이 즉시 PICK/PLACE (MES 가 설비 준비 후에만 MOVECMD 를 보내는 운영용)
- `jobType=EXCHANGE` 는 이 설정과 무관하게 항상 actionCmd 게이트 대기
- 현재값은 기동 로그에 1회 출력: `SequenceSettings — GeneralMoveEqpDirectExecute=...`

## 일반 반송 4경로 (ACS 는 amrSlot=1 고정)

| 경로 | 출발행 | 도착행 |
|------|--------|--------|
| 자재→설비 | UNLOAD@자재 slot1 (포트 PICK→슬롯 PLACE) | LOAD@EQP slot1 (슬롯 PICK DI0 → 설비 PLACE DI8/9) |
| 설비→자재 | UNLOAD@EQP slot1 (설비 PICK DI10/11 → 슬롯 PLACE DI4) | LOAD@자재 slot1 (슬롯 PICK → 자재 PLACE DI12/13) |
| 자재→자재 | UNLOAD@자재 slot1 | LOAD@자재 slot1 |
| 설비→설비 | UNLOAD@EQP slot1 | LOAD@EQP slot1 |

reply 시퀀스(각 구간): `ACCEPTED → EXECUTING → ARRIVED(도킹) → COMPLETED(jobType 포함, resultCode 0)`

## ACS 측 메모 (이 작업 범위 밖 — 전달용)

ACS 저장소 `docs/ACS-AMR_mqtt_exchange.md` §moveCmd 의 `amrSlot` 행
("UNLOAD(픽업)=투입슬롯, LOAD(반납)=회수슬롯")에 **"EXCHANGE 한정"** 명시 필요.
