# ACS-AMR MQTT exchangeCmd 인터페이스 정의서 (초안)

| 항목 | 내용 |
|------|------|
| 문서 상태 | **초안 v0.2 (협의용)** — [9. 협의 필요 항목](#9-협의-필요-항목-open-issues) 확정 전까지 구현 착수 금지 |
| 작성일 | 2026-08-11 |
| 개정 이력 | v0.2 (2026-08-11): 협의 #1 확정 — Loc→NodeId 변환은 ACS 담당, 단일 NodeId 전달 |
| 상위 사양 | 나무가 ACS_MES 매거진 교체 시나리오 사양서 v2 (2026-07-29) |
| 관련 문서 | `docs/ACS-AMR_mqtt_movecmd.md`, `docs/mqtt_interface.md`, `docs/vehicle_alarm.md`, `docs/vehicle_abnormal.md` |

MES↔ACS `EXCHANGECMD`(매거진 교환) 시나리오를 수행하기 위해 ACS↔AMR 간에 추가되는 MQTT 인터페이스를 정의한다. ACS는 본 인터페이스의 AMR 단계 보고를 근거로 MES에 `JOBREPORT`(Step 10~60)를 생성한다.

---

## 1. 개요

- **토픽 (기존 재사용)**
  - ACS → AMR: `amr/{ClientId}/command` — `exchangeCmd`, `actionCmd`(확장), `cancelCmd`(신규)
  - AMR → ACS: `amr/{ClientId}/reply` — 단계 보고 확장 (`STEP_COMPLETE`, `CANCELED` 추가)
- **역할 분담**
  - MES↔ACS 메시지(EXCHANGECMD/JOBREPORT/ACTIONCMD/JOBCANCEL)는 **ACS가 처리·변환**
  - AMR은 ACS가 변환해 준 노드/슬롯 기준으로 **물리 시퀀스 수행 + 단계 보고**만 담당
- **Job 단위**: 교환 1건 = `jobId` 1개. AMR은 시퀀스 시작~종료까지 `jobId`를 유지하며 모든 보고에 포함한다.

## 2. 단계(Step) 매핑 — MES 사양 ↔ AMR 시퀀스

| MES Step | StepName | AMR 내부 시퀀스 | AMR→ACS 보고 | ACS→MES 보고 |
|:---:|----------|----------------|--------------|--------------|
| 10 | PICKUP_NEW | 픽업지 이동 → QR 보정 → 신규 매거진 PICK → AMR 슬롯 **1\|2** PLACE | `ACCEPTED` → `EXECUTING` | RECEIVE / START |
| 20 | MOVE_TO_EQUIP | 설비 노드 이동 → 도착 | `ARRIVED` (step=20) | ARRIVED |
| — | (게이트1) | **actionCmd(type=UNLOAD) 대기** | — | — |
| 30 | UNLOAD_OLD | QR 보정 → 기존 매거진 PICK(설비) → AMR 슬롯 **3\|4** PLACE | `STEP_COMPLETE` (step=30, carrierSlot=3\|4) | STEP_COMPLETE |
| — | (게이트2) | **actionCmd(type=LOAD) 대기** | — | — |
| 40 | LOAD_NEW | 신규 매거진 PICK(AMR 슬롯 1\|2) → 설비 PLACE | `STEP_COMPLETE` (step=40, carrierSlot=1\|2) | STEP_COMPLETE |
| 50 | RETURN_OLD | 반납지 이동 → QR 보정 → 기존 매거진 PICK(슬롯 3\|4) → 반납지 PLACE | `STEP_COMPLETE` (step=50, carrierSlot=3\|4) | STEP_COMPLETE |
| 60 | DONE | Cobot 홈 복귀 → 대기 전환 | `COMPLETED` (step=60) | COMPLETE |

> **주의 (사양서 원문):** UNLOAD_OLD·LOAD_NEW는 설비 기구 상태 때문에 반드시 설비의 후속 요청(FINAL_UNLOAD_REQUEST / UPLOAD_REQUEST)이 MES→ACS `ACTIONCMD`로 중계된 뒤에만 실행한다. AMR은 해당 게이트에서 `actionCmd`를 수신할 때까지 다음 단계로 진행하지 않는다.

## 3. 처리 흐름

```
MES                ACS                       AMR
 │ EXCHANGECMD      │                         │
 │─────────────────▶│  exchangeCmd            │
 │                  │────────────────────────▶│ 검증(연결/Idle/매핑/슬롯)
 │◀─ RECEIVE(10) ───│◀─── ACCEPTED ───────────│
 │◀─ START(10) ─────│◀─── EXECUTING ──────────│ 픽업지 이동·적재 (슬롯1|2)
 │◀─ ARRIVED(20) ───│◀─── ARRIVED(20) ────────│ 설비 도착
 │ FINAL_UNLOAD_REQ │                         │ (게이트1 대기)
 │─────────────────▶│  actionCmd(UNLOAD)      │
 │                  │────────────────────────▶│ 기존 매거진 회수 → 슬롯3|4
 │◀ STEP_COMPLETE(30)◀── STEP_COMPLETE(30) ───│
 │ UPLOAD_REQUEST   │                         │ (게이트2 대기)
 │─────────────────▶│  actionCmd(LOAD)        │
 │                  │────────────────────────▶│ 신규 매거진 투입 ← 슬롯1|2
 │◀ STEP_COMPLETE(40)◀── STEP_COMPLETE(40) ───│
 │                  │                         │ 반납지 이동·하역 (슬롯3|4)
 │◀ STEP_COMPLETE(50)◀── STEP_COMPLETE(50) ───│
 │◀─ COMPLETE(60) ──│◀─── COMPLETED(60) ──────│ 홈 복귀·대기
```

## 4. exchangeCmd — 교환 명령 (ACS → AMR)

**토픽:** `amr/{ClientId}/command`

```json
{
  "cmdId": "20260811_103000_001",
  "command": "exchangeCmd",
  "jobId": "EX20260706103000123",
  "loadSourceNode": "N0010",
  "equipNode": "N0003",
  "unloadDestNode": "N0011",
  "port": "RIGHT",
  "model": "CF203W",
  "loadSlot": 1,
  "unloadSlot": 3,
  "loadSourcePortType": "MATERIAL",
  "unloadDestPortType": "MATERIAL"
}
```

| 필드 | 타입 | 필수 | 설명 |
|------|------|:---:|------|
| `cmdId` | string | O | 명령 일련번호 (기존 규칙 동일) |
| `command` | string | O | `exchangeCmd` 고정 |
| `jobId` | string | O | ACS Exchange Job ID (= MES EXCHANGECMD의 JobID). 모든 보고에 그대로 반환 |
| `loadSourceNode` | string | O | 신규 매거진 픽업 위치 NodeId (**확정: LoadSourceLoc→NodeId 변환은 ACS 담당, 단일 NodeId 전달**) |
| `equipNode` | string | O | 대상 설비 NodeId (EquipID→NodeId 변환은 ACS 담당) |
| `unloadDestNode` | string | O | 기존 매거진 반납 위치 NodeId |
| `port` | string | O | 설비 포트 (LEFT / RIGHT) — MES EXCHANGECMD의 Port |
| `model` | string | O | 매거진 모델 — LOAD/UNLOAD Model Offset 보정에 사용 |
| `loadSlot` | int | O | 신규 매거진 AMR 슬롯 (**1\|2**, ACS 자동배정 결과) |
| `unloadSlot` | int | O | 회수 매거진 AMR 슬롯 (**3\|4**, ACS 자동배정 결과) |
| `loadSourcePortType` | string | - | 픽업지 포트 유형 (기본 MATERIAL) |
| `unloadDestPortType` | string | - | 반납지 포트 유형 (기본 MATERIAL) |

**수락 조건 (모두 만족 시 ACCEPTED):** AMR Modbus 연결 · WorkStatus=Idle(다른 시퀀스 없음) · Cobot Auto/Run · 3개 노드 모두 위치 태그 매핑 존재 · `loadSlot` 비어 있음 · `unloadSlot` 비어 있음(센서 기준)

## 5. Reply 확장 — 단계 보고 (AMR → ACS)

**토픽:** `amr/{ClientId}/reply`

기존 `CommandReply`에 4개 필드를 추가한다 (기존 moveCmd 응답에는 영향 없음 — null 생략):

| 추가 필드 | 타입 | 설명 |
|-----------|------|------|
| `jobId` | string | exchangeCmd의 jobId 그대로 |
| `step` | int | 단계 코드 (10/20/30/40/50/60) — MES 사양과 동일 값 |
| `stepName` | string | PICKUP_NEW / MOVE_TO_EQUIP / UNLOAD_OLD / LOAD_NEW / RETURN_OLD / DONE |
| `carrierSlot` | int | 해당 단계에서 사용한 AMR 슬롯 (STEP_COMPLETE 30/40/50에서 필수) |

**status 목록 (기존 + 신규):**

| status | 신규 | 의미 | ACS→MES 변환 |
|--------|:---:|------|--------------|
| `ACCEPTED` | | 명령 수락 (step=10) | JOBREPORT RECEIVE |
| `EXECUTING` | | 시퀀스 시작(출발) (step=10) | JOBREPORT START |
| `ARRIVED` | | 설비 도착 (step=20) | JOBREPORT ARRIVED |
| `STEP_COMPLETE` | ★ | 단계 완료 (step=30/40/50 + carrierSlot) | JOBREPORT STEP_COMPLETE |
| `COMPLETED` | | 전체 완료 (step=60) | JOBREPORT COMPLETE |
| `FAILED` | | 실패 종결 (resultCode 참조) | JOBREPORT COMPLETE + ErrorCode |
| `REJECTED` | | 수락 거부 | (ACS 내부 처리 / 재시도 판단) |
| `CANCELED` | ★ | 취소 처리 완료 ([7절](#7-cancelcmd--취소-명령-acs--amr-신규)) | JOBREPORT CANCEL |

**예시 — STEP_COMPLETE (UNLOAD_OLD):**

```json
{
  "cmdId": "20260811_103000_001",
  "jobId": "EX20260706103000123",
  "status": "STEP_COMPLETE",
  "step": 30,
  "stepName": "UNLOAD_OLD",
  "carrierSlot": 3,
  "resultCode": 0,
  "message": "기존 매거진 회수 완료 (슬롯3)",
  "timestamp": "2026-08-11T10:31:20.000Z"
}
```

## 6. actionCmd 확장 — 게이트 허가 (ACS → AMR)

기존 `actionCmd`(설비포트 포트 지정)에 **`type`, `jobId` 필드를 추가**한다.

```json
{
  "cmdId": "20260811_103100_002",
  "command": "actionCmd",
  "jobId": "EX20260706103000123",
  "type": "UNLOAD"
}
```

| 필드 | 필수 | 설명 |
|------|:---:|------|
| `type` | O(교환 시) | `UNLOAD` = 기존 매거진 취출 허가 (게이트1) / `LOAD` = 신규 매거진 투입 허가 (게이트2) |
| `jobId` | O(교환 시) | 진행 중 jobId와 일치해야 수용. 불일치 시 무시(로그만) |
| `port`, `amrSlot` | - | 기존 moveCmd용 필드 — 교환 시퀀스에서는 무시 |

**수용 조건:** AMR은 자신의 현재 게이트 상태와 type이 일치할 때만 수용한다 (게이트1 대기 중 `UNLOAD`만, 게이트2 대기 중 `LOAD`만). 그 외는 무시하고 로그를 남긴다. *(ACS도 사양상 Step=20 상태에서만 UNLOAD, Step=30 상태에서만 LOAD를 수용하므로 이중 방어)*

**게이트 대기 정책 (협의 #2):** 기본 타임아웃 안(案) — 설비 준비는 수 분 이상 걸릴 수 있으므로 **기본 무제한 대기 + 경고 로그(120초 주기)**, 설정으로 상한 지정 가능. 상한 초과 시 `FAILED`(resultCode=32) 종결.

## 7. cancelCmd — 취소 명령 (ACS → AMR, 신규)

MES `JOBCANCEL`을 ACS가 판정(C1~C5)한 뒤, **AMR 물리 동작이 필요한 경우에만** 전달한다.

```json
{
  "cmdId": "20260811_104000_003",
  "command": "cancelCmd",
  "jobId": "EX20260706103000123",
  "returnNode": "N1001"
}
```

| 필드 | 필수 | 설명 |
|------|:---:|------|
| `jobId` | O | 취소 대상 Job ID |
| `returnNode` | - | 적재 후 취소 시 복귀 노드. 생략 시 자동충전 노드(ChargeNodeId) 사용 (협의 #3) |

**AMR 처리 (사양서 판정표 C1~C5 대응):**

| 판정 | AMR 상태 | AMR 동작 | 응답 |
|:---:|----------|----------|------|
| C1 | 해당 없음 (배차 전 취소는 ACS 내부 종결 — AMR 미전달) | — | — |
| C2 | 픽업 전 (PICKUP_NEW의 적재 완료 전) | 시퀀스 중단 → AMR 정지 → Idle 복귀 | `CANCELED` (resultCode=0) |
| C3 | **적재 후** (매거진 탑재 상태 전 구간) | 시퀀스 중단 → `returnNode`(충전소) 이동 → **차량 ALARM 상태 진입**(경광등 Red+부저, abnormal 300 보고) → 작업자 실물 회수 + Reset으로 해제 | `CANCELED` (resultCode=0) |
| C4 | 이미 종료(COMPLETED/FAILED/CANCELED) 또는 jobId 불일치 | 없음 | `CANCELED` 거부 (resultCode=40 CANCEL_REJECTED) |
| C5 | (배칭 페어 종결은 ACS 내부 처리 — AMR 미전달) | — | — |

## 8. 오류 코드 정의

### 8.1 Reply resultCode (기존 + 신규)

| resultCode | status | 구분 | 의미 |
|:---:|--------|:---:|------|
| 0 | - | 기존 | 정상 |
| 2 | REJECTED | 기존 | 지원하지 않는 command |
| 10 | REJECTED | 기존 | AMR Modbus 미연결 |
| 11 | REJECTED | 기존 | 작업 중 (Idle 아님) |
| 20 | REJECTED | 기존 | NodeId 위치 태그 매핑 없음 (3개 노드 중 하나라도) |
| **21** | REJECTED | ★신규 | **슬롯 상태 불일치** — loadSlot 점유 중 또는 unloadSlot 점유 중 (수락 단계 거부) |
| **22** | REJECTED | ★신규 | Cobot 준비 안 됨 (Manual/정지/미연결) |
| **30** | FAILED | ★신규 | **MAGAZINE_NOT_FOUND** — 픽업지(LoadSourceNode)에 매거진 부재. 재시도 없이 즉시 종결 *(→ ACS는 MES에 COMPLETE + ErrorCode=MAGAZINE_NOT_FOUND)* |
| **31** | FAILED | ★신규 | 시퀀스 중 슬롯/센서 상태 불일치 (예: 투입 직전 슬롯1\|2 매거진 소실) |
| **32** | FAILED | ★신규 | actionCmd 게이트 대기 상한 초과 (상한 설정 시에만) |
| **40** | CANCELED | ★신규 | **CANCEL_REJECTED** — 취소 불가 (이미 종료 상태 / jobId 불일치) |
| 99 | FAILED | 기존 | 내부 예외 |

### 8.2 차량 알람 신규 (`docs/vehicle_alarm.md` 에 추가)

| 코드 | 이름 | 조건 | 동작 |
|------|------|------|------|
| **ERR-114** | Pickup Source Magazine Not Found | 교환 픽업지에서 매거진 미검출 (깊이감지/센서) | 시퀀스 종결(FAILED 30), 경광등 Red+부저, Reset으로 해제 |
| **ERR-115** | Exchange Slot State Mismatch | 시퀀스 중 지정 슬롯 상태가 기대와 불일치 | 시퀀스 종결(FAILED 31), Red+부저 |
| **ERR-116** | ActionCmd Wait Timeout | 게이트 대기 상한 초과 (상한 설정 시) | 시퀀스 종결(FAILED 32), Red+부저 |

*(참고: ERR-110~113 슬롯/포트 알람은 기존 구현 재사용 — 교환 시퀀스의 PICK/PLACE 단계에서 동일하게 발동)*

### 8.3 Status abnormal 신규 (`docs/vehicle_abnormal.md` 에 추가)

| 코드 | 타입 | 조건 | 해제 |
|------|------|------|------|
| **300** | EXCHANGE_CANCEL_HOLD | 적재 후 취소(C3)로 충전소 복귀 완료, 작업자 실물 회수 대기 (latched) | 작업자가 매거진 회수 후 **Reset 짧게** → 정상 복귀. 새 Job 시작 시 fallback 해제 |

status 토픽의 `abnormal` 객체로 보고: `{"code":"300","type":"EXCHANGE_CANCEL_HOLD","node":"N1001","timestamp":"…"}`

## 9. 협의 필요 항목 (Open Issues)

| # | 항목 | 초안 가정 | 확정 필요 사항 |
|:---:|------|-----------|----------------|
| 1 | ~~LoadSourceLoc/UnloadDestLoc/EquipID → NodeId 변환~~ | **✅ 확정 (2026-08-11)** | **ACS가 변환해 단일 NodeId로 전달.** 복수 후보(candidates)의 노드 선정은 ACS 책임 — AMR은 전달받은 노드(내부 슬롯 1\|2 순회 탐색 포함)만 확인하고, 미검출 시 즉시 FAILED(30, MAGAZINE_NOT_FOUND) 종결. 재시도 없음(재요청은 MES 몫) |
| 2 | 게이트(actionCmd) 대기 정책 | 무제한 대기 + 주기 경고, 설정 상한 옵션 | 타임아웃 값, 초과 시 처리(FAILED 32 vs 계속 대기) |
| 3 | C3 취소 복귀 노드 | cancelCmd.returnNode, 생략 시 자동충전 노드 | 충전소 복귀 vs 별도 대기 장소 |
| 4 | START(EXECUTING) 보고 시점 | AMR 시퀀스 시작(이동 개시) 시점 | ACS "배차" 개념과의 시점 정합 (사양상 START는 슬롯 배정 완료 후) |
| 5 | 슬롯 배정 주체 | ACS가 loadSlot/unloadSlot 지정, AMR은 검증만 | AMR 자율 배정 허용 여부 (사양상 ACS 자동배정이므로 초안은 ACS 지정) |
| 6 | 픽업지·반납지 QR/티칭 | 기존 자재포트 티칭·DI 재사용 | 버퍼 위치가 신규 스테이션이면 티칭포인트/거점/매핑 추가 필요 (사용자 매뉴얼 5장) |
| 7 | 기존 moveCmd와의 공존 | exchangeCmd 진행 중 moveCmd는 REJECTED(11) | 우선순위·큐잉 정책 |

## 10. 구현 매핑 (수정 대상 소스)

| 구성 요소 | 파일 | 수정 내용 |
|-----------|------|-----------|
| 명령 모델 | `AMR/Models/AmrCommand.cs` | jobId, exchange 필드(3노드/슬롯/type) 추가 또는 ExchangeCommand 신설 |
| 응답 모델 | `AMR/Models/CommandReply.cs` | jobId, step, stepName, carrierSlot 필드 추가 |
| MQTT 수신/발행 | `AMR/Service/MqttService.cs` | exchangeCmd/cancelCmd 파싱, STEP_COMPLETE/CANCELED 발행 |
| 명령 라우팅 | `AMR/Service/MainSequenceService.cs` | exchangeCmd 수락 검증(슬롯 포함), cancelCmd 라우팅 |
| 시퀀스 엔진 | `AMR/Service/MoveSequenceRunner.cs` | **EXCHANGE 다중 목적지 시퀀스 신설**, 2중 게이트, 슬롯 고정 정책, 단계 보고 훅, C2/C3 취소 처리 |
| 단계 정의 | `AMR/Enums/SequenceStep.cs` | 교환 단계 추가 |
| 알람 | `AMR/Models/Alarm.cs`, `AlarmService.cs` | ERR-114/115/116 추가 |
| 이상 보고 | `AMR/Models/AbnormalInfo.cs` 사용부 | EXCHANGE_CANCEL_HOLD(300) 보고·latch·Reset 해제 |
| 취소 복귀 | `AMR/Service/IdleChargeService.cs` 연계 | C3 충전소 복귀 시 자동충전 트리거와 충돌 방지 |
| 테스트 UI | `AMR.Web/Pages/Sequence.cshtml(.cs)` | EXCHANGE 시퀀스 실행/게이트 수동 주입(type)/단계 표시 |
| 문서 | `docs/mqtt_interface.md`, `docs/vehicle_alarm.md`, `docs/vehicle_abnormal.md` | 본 정의서 반영 |
