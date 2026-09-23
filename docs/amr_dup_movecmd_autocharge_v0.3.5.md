# AMR 차상 SW v0.3.5 — 중복 moveCmd 멱등 처리 + 자동 충전 보류 규칙

배경: 2026-09-15 현장(EXCHANGE 잡 0090·0092). ACS 정체복구 로직이 같은 moveCmd 를 0.1~0.8초 뒤 한 번 더 보냈고,
AMR 이 REJECTED(11) 로 답해 ACS 가 픽업 실패로 롤백 → 실제 COMPLETED 폐기 → 재배차 반복. 이후 AMR 이 매거진을 실은 채 자동 충전으로 이동.
ACS 는 수정하지 않고 AMR 에서 아래 두 가지를 고쳤다.

## R1. 실행 중 명령과 동일한 moveCmd 재수신 → ACCEPTED 재응답 (`MainSequenceService.HandleMoveCmdAsync`)

| 조건 | 동작 | reply |
|---|---|---|
| 시퀀스 실행 중 + `cmdId`·`nodeId`·`port`·`jobType`·`amrSlot` **5개 모두 동일** | 재전송으로 간주 — 새 시퀀스 시작 안 함 | `ACCEPTED` 1회 재발행 (ARRIVED/COMPLETED 는 재발행 안 함) |
| 시퀀스 실행 중 + 5개 중 하나라도 다름 | 기존과 동일 | `REJECTED(11)` |
| Idle (완료 직후 포함) | 기존과 동일 — 정상 수락 | ACCEPTED → EXECUTING → … |

- EXCHANGE 는 STEP 10/20/50 이 같은 cmdId 를 쓰므로 cmdId 만으로 판단하지 않는다.
- 로그: `중복 moveCmd 무시(재전송): cmdId=…, nodeId=…` (INF)

## R2. 자동 충전 보류 (`IdleChargeService`)

아래 중 하나라도 해당하면 자동 충전을 트리거하지 않고, 해소 후 Idle 시간을 **처음부터 다시** 센다.

| 보류 사유 | 판정 |
|---|---|
| `잡 진행 중` | 설비 앞 도킹 후 actionCmd/다음 명령 대기(ExchangeDocked). 시퀀스 실행 중은 원래부터 트리거 안 함 |
| `슬롯 점유` | AMR 슬롯 1~4 중 하나라도 MzDetect ON (시뮬레이션은 가상 슬롯) |
| `리셋 복구 중` | 리셋 스위치 복구 시퀀스 진행 중 (v0.3.3) |

- 로그: `자동 충전 보류: {사유}` — 사유가 바뀔 때만 1회 (반복 출력 없음), 해소 시 `자동 충전 보류 해제 (…)`.
- 충전 노드 기록이나 실제 충전 중이 아니면 재트리거(v0.3.3) 등 기존 동작은 유지.

## 검증 (2026-09-15, 시뮬레이터 + MQTT)

| # | 상황 | 결과 |
|---|---|---|
| 1 | 주행 중 같은 moveCmd 재수신 | ACCEPTED 재응답, 시퀀스 1개, ARRIVED/COMPLETED 각 1회 — PASS |
| 2 | 주행 중 같은 cmdId·다른 nodeId | REJECTED(11) — PASS |
| 3 | 완료 직후 같은 cmdId 로 EXCHANGE moveCmd | 정상 수락·도킹 — PASS |
| 4 | slot1 점유 idle | 트리거 없음 — PASS |
| 5 | 설비 앞 actionCmd 대기 | 트리거 없음 — PASS |
| 6 | 슬롯 모두 비움 idle | 자동 충전 정상 — PASS |

---
## v0.3.6 (2026-09-23) — 충전 노드 재트리거 오판 수정

현장: 충전소 도킹 상태에서 배터리 81%(만충 근처) → BMS 가 Charging/Discharging 을 오감(전류 0~0.1A) → v0.3.3 의
"충전 노드인데 Discharging 이면 재트리거" 규칙이 40초마다 CHARGE 시퀀스를 반복.

| 상황 | 동작 |
|---|---|
| 충전 노드 + 지금 Charging | 트리거 안 함 (기존) |
| 충전 노드 + 마지막 충전 이동 이후 Charging 이 한 번이라도 관측됨 | **도킹 성공으로 보고 트리거 안 함** (트리클 충전) |
| 충전 노드 + Charging 이 전혀 관측 안 됨 | 재트리거하되 **최대 3회** — 로그 `… 자동 충전 재트리거 (n/3)`; 초과 시 `자동 충전 재시도 3회 초과 — 충전기/도킹 확인 필요` 1회 후 정지. 충전 아닌 새 작업이 시작되면 카운터 초기화 |

- Charging 관측은 MainSequenceService 상태 루프(1초)에서 기록 — 5초 폴링으로는 짧은 Charging 을 놓치기 때문.
- 검증(시뮬레이터, Charging 관측 불가 조건): 최초 1회 + 재시도 3회 = COMPLETED 4회 후 정지 — PASS.
