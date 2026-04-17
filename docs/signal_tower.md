# Signal Tower (경광등) 운영 조건

## 개요

AMR 상단에 장착된 3색 경광등(Signal Tower)으로 현재 시스템 상태를 시각적으로 표시한다.
I/O 모듈(LS산전 XEL-BSSRT)의 Coil 출력을 통해 제어한다.

## 램프 구성

| 색상 | I/O 주소 | Coil 번호 | 코드 매핑 |
|------|----------|-----------|-----------|
| Red | Y000 | 0 | `TowerLampRed` |
| Orange | Y001 | 1 | `TowerLampYellow` |
| Green | Y002 | 2 | `TowerLampGreen` |
| Buzzer | Y003 | 3 | `TowerLampBuzzer` |

## 운영 조건

### Green — 정상 운영

| 조건 | Green | Orange | Red | Buzzer |
|------|-------|--------|-----|--------|
| AMR 대기 (Idle) | ON | ON     | OFF | OFF |
| 시퀀스 정상 완료 | ON | ON     | OFF | OFF |

### Orange — 작업 중

| 조건 | Green | Orange | Red | Buzzer |
|------|----|--------|-----|--------|
| AMR 이동 중 | ON | OFF    | OFF | OFF |
| 시퀀스 실행 중 (Cobot 작업 등) | ON | OFF    | OFF | OFF |
| 베터리 20%이하| ON | 점멸(1초) | OFF | OFF |

### Red — 이상 / 정지

| 조건 | Green | Orange | Red | Buzzer |
|------|-------|--------|-----|--------|
| 시퀀스 실패 (Faulted) | OFF | OFF | ON | ON |
| EMO (비상정지) 활성 | OFF | OFF | ON | ON |
| 통신 끊김 (AMR/Cobot/I/O) | OFF | OFF | ON | OFF |
| 타임아웃 (도착 대기 등) | OFF | OFF | ON | ON |

## 우선순위

여러 조건이 동시에 해당될 경우 아래 우선순위를 따른다.

1. **Red** (최우선) — 이상 상태는 항상 Red 표시
2. **Orange** — 작업 진행 중
3. **Green** — 정상 대기

## 버저 운영

- 이상 발생(Red) 시 버저 동시 ON
- Reset 스위치(X001) 입력 시 버저 OFF 및 이상 상태 해제
- 통신 끊김은 버저 없이 Red 램프만 점등
