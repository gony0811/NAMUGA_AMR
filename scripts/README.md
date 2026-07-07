# AMR 자동 시작 설정

PC 부팅 시 AMR.Web 을 자동 실행하는 스크립트.
**로그온 불필요** — 부팅 직후, 아무도 로그인하지 않아도 시작됩니다.

## 사전 준비 — RabbitMQ 네이티브 Windows 서비스 설치 (한 번만)

이전 버전은 Docker Desktop 으로 RabbitMQ 컨테이너를 띄웠는데,
**Docker Desktop 은 데스크탑 앱이라 사용자 세션이 있어야만 동작** 합니다.
부팅 직후(로그인 안 된 상태)부터 켜지게 하려면 RabbitMQ 를 네이티브 Windows
서비스로 설치해야 합니다.

1. **Erlang/OTP** 설치: https://www.erlang.org/downloads (Windows 64-bit)
2. **RabbitMQ Server (Windows)** 설치: https://www.rabbitmq.com/install-windows.html
   - 설치 시 `RabbitMQ` Windows 서비스가 자동 등록 (시작 유형: 자동)
3. MQTT 플러그인 활성화 — **시작 메뉴 → "RabbitMQ Command Prompt (sbin dir)"** 열고:
   ```
   rabbitmq-plugins enable rabbitmq_mqtt rabbitmq_management
   ```
4. 서비스 재시작:
   ```
   net stop RabbitMQ
   net start RabbitMQ
   ```
5. 확인:
   - 관리 UI: http://localhost:15672 (guest/guest)
   - MQTT 포트: `Test-NetConnection localhost -Port 1883` → True

기존 Docker 컨테이너가 떠 있다면 정리:
```powershell
docker compose -f C:\Users\eggplant\Documents\GitHub\NAMUGA_AMR\docker\docker-compose.yml down
```

## 동작 흐름

1. **PC 부팅** (로그온 없이)
2. Task Scheduler 가 30초 후 `start-amr.ps1` 실행 (S4U 비대화형 세션)
3. RabbitMQ Windows 서비스 상태 확인 (필요 시 시작)
4. AMR.Web 실행 (http://0.0.0.0:5200)

로그: `<레포루트>\Logs\autostart\start-amr.log`

## 설치

**관리자 PowerShell** 에서 한 번만 실행:

```powershell
cd C:\Users\eggplant\Documents\GitHub\NAMUGA_AMR\scripts
.\install-autostart.ps1
```

다음 부팅부터 자동 시작됩니다. (로그온 불필요)

## 즉시 테스트

```powershell
Start-ScheduledTask -TaskName "AMR Auto Start"
```

로그 모니터링:

```powershell
Get-Content -Wait -Tail 30 "..\Logs\autostart\start-amr.log"
```

## 해제

```powershell
.\uninstall-autostart.ps1
```

## Published 빌드 사용 (권장)

기본은 `dotnet run` 으로 동작하는데, 매 실행마다 빌드 시간이 듭니다.
한 번 publish 해두면 스크립트가 자동으로 그 exe 를 우선 사용합니다.

```powershell
cd C:\Users\eggplant\Documents\GitHub\NAMUGA_AMR
dotnet publish AMR.Web\AMR.Web.csproj -c Release -o AMR.Web\bin\Release\net8.0\publish
```

## 문제 해결

| 증상 | 확인 |
|---|---|
| 자동 시작 안 됨 | Task Scheduler 에서 `AMR Auto Start` 작업 상태/마지막 실행 결과 확인 |
| 작업이 "0x2" 로 실패 | `start-amr.ps1` 경로 또는 PowerShell 실행 정책 확인 |
| MQTT 연결 실패 | `Get-Service RabbitMQ` 상태, `rabbitmq-plugins list` 로 mqtt 활성 여부 확인 |
| AMR.Web 즉시 종료 | `Logs\autostart\web-stderr.log` 확인 |
| 포트 5200 사용 중 | `netstat -ano \| findstr 5200` |
| 카메라 인식 안 됨 | S4U 비대화형 세션에서 USB 접근 제한 가능 — 대화형 모드로 일시 변경해서 테스트 |

## 비대화형(S4U) 실행 관련 주의

작업 스케줄러가 사용자 본인 계정으로 **비대화형(S4U)** 으로 실행합니다.
즉, 데스크탑 세션 없이 백그라운드에서 돕니다.

- ✅ HTTP 서버, Modbus TCP, MQTT, SQLite, 로그 파일 — 모두 정상 동작
- ⚠️ USB 카메라(Orbbec): 대부분의 드라이버는 비대화형 세션에서도 동작하지만
  드라이버에 따라 거부될 수 있음. 그럴 때는 `install-autostart.ps1` 의
  `-LogonType S4U` 를 `Interactive` 로 바꾸고 자동 로그인을 다시 활성화해야 함.
- ⚠️ GUI 가 필요한 진단 도구는 RDP 로 접속해서 별도로 실행

## 이전 Docker 기반 구성에서 마이그레이션

기존에 자동 시작이 등록된 상태라면 그냥 다시 install 하면 됩니다 — 기존 작업이 자동으로 제거되고 새 트리거(`AtStartup`/S4U)로 교체됩니다.

```powershell
.\install-autostart.ps1
```

이후 Docker Desktop 자동 시작은 꺼도 됩니다:
- Docker Desktop → Settings → General → ❌ "Start Docker Desktop when you sign in"
