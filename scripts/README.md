# AMR 자동 시작 설정

PC 시작 시 Docker (RabbitMQ/MQTT) 와 AMR.Web 을 자동 실행하는 스크립트.

## 동작 흐름

1. Windows 로그온
2. Task Scheduler 가 1분 후 `start-amr.ps1` 실행
3. Docker Desktop 기동 → engine 준비 대기 (최대 120초)
4. `docker compose up -d` (RabbitMQ + MQTT)
5. AMR.Web 실행 (http://0.0.0.0:5200)

로그: `<레포루트>\Logs\autostart\start-amr.log`

## 설치

**관리자 PowerShell** 에서 한 번만 실행:

```powershell
cd C:\Users\eggplant\Documents\GitHub\NAMUGA_AMR\scripts
.\install-autostart.ps1
```

다음 로그온부터 자동 시작됩니다.

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

## PC 부팅 시점부터 동작하려면 (자동 로그인)

Task Scheduler 트리거는 "로그온 시" 라서 Windows 자동 로그인이 활성화돼야 PC 부팅 → 로그인 → 자동 시작 흐름이 완성됩니다.

- `Win + R` → `netplwiz`
- 해당 사용자 선택 → **"사용자 이름과 암호를 입력해야 함" 체크 해제**
- 비밀번호 2회 입력
- 재부팅하여 확인

## Docker Desktop 자동 시작 (선택)

스크립트 안에서도 Docker Desktop 을 띄우지만, 더 빠르게 시작하려면 Docker Desktop 설정에서:

- Docker Desktop 우상단 톱니바퀴 → Settings → General
- ✅ **"Start Docker Desktop when you sign in to your computer"** 체크

## Published 빌드 사용 (선택, 권장)

기본은 `dotnet run` 으로 동작하는데, 매 실행마다 빌드 시간이 듭니다. 한 번 publish 해두면 스크립트가 자동으로 그 exe 를 우선 사용합니다.

```powershell
cd C:\Users\eggplant\Documents\GitHub\NAMUGA_AMR
dotnet publish AMR.Web\AMR.Web.csproj -c Release -o AMR.Web\bin\Release\net8.0\publish
```

## 문제 해결

| 증상 | 확인 |
|---|---|
| 자동 시작 안 됨 | Task Scheduler 에서 `AMR Auto Start` 작업 상태 확인 |
| Docker engine 준비 timeout | Docker Desktop 자체가 실행되는지, WSL2/Hyper-V 설정 |
| AMR.Web 즉시 종료 | `Logs\autostart\start-amr.log` 의 `[web]`/`[web-err]` 라인 |
| 포트 5200 사용 중 | 다른 인스턴스 동작 중인지 `netstat -ano \| findstr 5200` |
