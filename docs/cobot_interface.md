# Cobot Interface
## Cobot과 제어 PC 간 인터페이스 정의서

-- [2] Teaching Position 명령/응답 DI/DO 매핑
----------------------------------------------------------------------
-- DI 입력 (AMR PC → 로봇)
-- DI0~3:   AMR PICK 슬롯1~4
-- DI4~7:   AMR PLACE 슬롯1~4
-- DI8~9:  설비포트 Loading 슬롯1~2
-- DI10~11: 설비포트 Unloading 슬롯1~2
-- DI12~13: 자재포트 Loading 슬롯1~2
-- DI14~15: 자재포트 Unloading 슬롯1~2
-- DI16:    설비포트 QR 스캔
-- DI17:    자재포트 QR 스캔
-- DI25:    Home
-- DO 출력 (로봇 → AMR PC)
-- DO0: Robot Busy (동작 중)
-- DO1: Robot Complete (동작 완료)
-- DO2: Robot Error (에러)
----------------------------------------------------------------------
-- [3] 위치 오프셋 변수 (Modbus AI에서 수신)
----------------------------------------------------------------------
--AI0: dx
--AI1: dy
--AI2: dz
