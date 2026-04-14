# ACS-AMR MQTT moveCmd 인터페이스

ACS에서 MQTT `moveCmd`를 수신하여 AMR을 지정 위치로 이동시키는 인터페이스 정의

---

## 개요

ACS가 `amr/{ClientId}/command` 토픽으로 `moveCmd`를 전송하면, AMR은 **위치 태그 매핑 테이블**에서 `NodeId`에 해당하는 `TaskIndex`/`JobIndex`를 조회하여 Modbus 레지스터에 기록하고 이동을 실행한다.

---

## 처리 흐름

```
ACS                           AMR
 │                             │
 │  moveCmd (NodeId=N0001)     │
 │ ──────────────────────────> │
 │                             ├─ 1. AMR Modbus 연결 확인
 │                             ├─ 2. WorkStatus == Idle 확인
 │                             ├─ 3. DB에서 NodeId → TaskIndex/JobIndex 조회
 │                             ├─ 4. Modbus HR 31 ← TaskIndex
 │                             ├─    Modbus HR 32 ← JobIndex
 │                             ├─    Modbus HR 30 ← Start(2)
 │  Reply (ACCEPTED/REJECTED)  │
 │ <────────────────────────── │
```

---

## 요청 메시지 (ACS → AMR)

**토픽:** `amr/{ClientId}/command`

```json
{
  "cmdId": "20260325_160501_001",
  "command": "moveCmd",
  "nodeId": "N0001",
  "port": "LEFT",
  "jobType": "LOAD"
}
```

| 필드 | 타입 | 필수 | 설명 |
|------|------|------|------|
| `cmdId` | string | O | 명령 일련번호 (년월일_시분초_일련번호) |
| `command` | string | O | `moveCmd` 고정 |
| `nodeId` | string | O | 위치 태그 (예: N0001) |
| `port` | string | - | 포트 위치 (LEFT / RIGHT) |
| `jobType` | string | - | 작업 유형 (LOAD / UNLOAD / EXCHANGE) |

---

## 응답 메시지 (AMR → ACS)

**토픽:** `amr/{ClientId}/reply`

```json
{
  "cmdId": "20260325_160501_001",
  "status": "ACCEPTED",
  "resultCode": 0,
  "message": "이동 명령 수락: N0001 (Task=1, Job=2)",
  "timestamp": "2026-04-14T07:30:00.000Z"
}
```

| 필드 | 타입 | 설명 |
|------|------|------|
| `cmdId` | string | 요청의 cmdId를 그대로 반환 |
| `status` | string | 처리 결과 상태 |
| `resultCode` | int | 결과 코드 (0 = 성공) |
| `message` | string | 상세 메시지 |
| `timestamp` | string | 응답 시각 (ISO 8601) |

---

## 응답 상태 및 결과 코드

| status | resultCode | 조건 | 설명 |
|--------|-----------|------|------|
| `ACCEPTED` | 0 | 정상 수행 | Task/Job 설정 완료, 이동 시작 |
| `REJECTED` | 2 | 지원하지 않는 명령 | `moveCmd` 외의 알 수 없는 command |
| `REJECTED` | 10 | AMR 미연결 | Modbus TCP 연결이 끊어진 상태 |
| `REJECTED` | 11 | 작업 중 | WorkStatus가 Idle이 아닌 상태 (Moving, Docking, Jog) |
| `REJECTED` | 20 | 매핑 없음 | NodeId에 해당하는 위치 태그 매핑이 DB에 없음 |
| `FAILED` | 99 | 내부 오류 | 명령 처리 중 예외 발생 |

---

## 위치 태그 매핑 테이블

설정 페이지(`/Settings`)에서 관리하며, SQLite(`amr.db`)에 영속화된다.

| 컬럼 | 타입 | 설명 |
|------|------|------|
| `Id` | int (PK) | 자동 증가 |
| `LocationTag` | string (Unique) | 위치 태그 (= AmrCommand.NodeId) |
| `TaskIndex` | int (0~65535) | Modbus Holding Register 31에 기록 |
| `JobIndex` | int (0~65535) | Modbus Holding Register 32에 기록 |
| `Description` | string (nullable) | 설명 (선택) |

### 매핑 예시

| LocationTag | TaskIndex | JobIndex | Description |
|-------------|-----------|----------|-------------|
| N0001 | 1 | 1 | 1번 스테이션 |
| N0002 | 1 | 2 | 2번 스테이션 |
| N0003 | 2 | 1 | 충전 스테이션 |

---

## Modbus 레지스터 매핑

이동 명령 실행 시 아래 Holding Register에 순차 기록:

| 순서 | 레지스터 | 주소 | 값 | 설명 |
|------|---------|------|-----|------|
| 1 | Task Index | HR 31 | 매핑 조회 값 | 실행할 Task 번호 |
| 2 | Job Index | HR 32 | 매핑 조회 값 | 실행할 Job 번호 |
| 3 | Execution Control | HR 30 | 2 (Start) | Task 실행 시작 |

### 실행 전 확인하는 Input Register

| 레지스터 | 주소 | 조건 | 설명 |
|---------|------|------|------|
| WorkStatus | IR 64 | Idle(1) | 대기 상태에서만 이동 수행 |

---

## 관련 소스 코드

| 구성 요소 | 파일 |
|----------|------|
| 명령 처리 | `AMR/Service/MainSequenceService.cs` — `HandleMoveCmdAsync()` |
| AMR 제어 | `AMR/Service/AmrService.cs` — `SetTaskIndexAsync()`, `SetJobIndexAsync()` |
| MQTT 수신 | `AMR/Service/MqttService.cs` — `OnCommandReceived` 이벤트 |
| MQTT 응답 | `AMR/Service/MqttService.cs` — `PublishReplyAsync()` |
| 매핑 DB | `AMR/Data/AmrDbContext.cs` — `LocationTagMappings` |
| 매핑 모델 | `AMR/Models/LocationTagMapping.cs` |
| 매핑 UI | `AMR.Web/Pages/Settings.cshtml` — 위치 태그 매핑 테이블 |
