using AMR.Enums;
using AMR.Models;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 사무실 시뮬레이션 모드 — 실장비(AMR/Cobot/카메라/센서) 없이 시퀀스를 검증한다.
/// 활성화 시 MoveSequenceRunner 의 하드웨어 동작이 이 클래스의 수동 확인 대기로 대체되고,
/// 슬롯/포트 센서는 여기 보관된 가상 상태를 사용한다. 웹 UI(Sequence 페이지)에서 제어.
/// </summary>
public class SequenceSimulator
{
    private readonly ILogger<SequenceSimulator> _logger;
    private readonly object _lock = new();
    private TaskCompletionSource<bool>? _pending;

    public SequenceSimulator(ILogger<SequenceSimulator> logger)
    {
        _logger = logger;
    }

    /// <summary>시뮬레이션 모드 활성화 여부</summary>
    public bool Enabled { get; private set; }

    /// <summary>현재 수동 확인 대기 중인 동작 설명 (없으면 null)</summary>
    public string? PendingAction { get; private set; }

    /// <summary>가상 AMR 슬롯 점유 상태 (index 0~3 = 슬롯 1~4, true=매거진 있음)</summary>
    public bool[] AmrSlots { get; } = new bool[4];

    /// <summary>가상 자재포트 슬롯 1 점유 상태 (true=매거진 있음)</summary>
    public bool MaterialSlot1 { get; set; }

    /// <summary>가상 자재포트 슬롯 2 점유 상태</summary>
    public bool MaterialSlot2 { get; set; }

    // ===== 가상 AMR 상태 (status 토픽 발행용) =====

    /// <summary>가상 현재 좌표 (X/Y m, Angle rad) — 이동 확인 시 목적지 노드 좌표로 갱신</summary>
    public RobotPose Pose { get; private set; } = new(0, 0, 0);

    /// <summary>가상 이동 중 여부 (RunState=Run/Stop, WorkState=Moving/Idle 반영)</summary>
    public bool Moving { get; private set; }

    /// <summary>가상 배터리 잔량 (%)</summary>
    public float BatteryPercent { get; set; } = 85f;

    /// <summary>가상 좌표 직접 설정</summary>
    public void SetPose(double x, double y, double angle)
    {
        Pose = new RobotPose((float)x, (float)y, (float)angle);
        _logger.LogInformation("[SIM] 가상 pose = ({X:F2}, {Y:F2}, {A:F2})", x, y, angle);
    }

    /// <summary>이동 시작 표시 (status: RunState=Run, WorkState=Moving)</summary>
    public void BeginMove() { Moving = true; }

    /// <summary>이동 완료 표시 + 목적지 좌표 반영 (좌표 미등록 노드면 pose 유지)</summary>
    public void EndMove(double? x, double? y, double? angle)
    {
        Moving = false;
        if (x is double px && y is double py)
            SetPose(px, py, angle ?? 0);
    }

    /// <summary>시뮬레이션용 RobotStatus 생성 — MainSequenceService 가 실 AMR 대신 status 발행에 사용</summary>
    public RobotStatus BuildRobotStatus() => new()
    {
        PowerState = PowerState.Normal,
        RobotState = Moving ? RobotState.Started : RobotState.Stopped,
        WorkStatus = Moving ? WorkStatus.Moving : WorkStatus.Idle,
        Pose = Pose,
        MapStatusPercent = 100f,
        Battery = new BatteryStatus
        {
            LevelPercent = BatteryPercent,
            Voltage = 27f,
            Current = 1f,
            TemperatureCelsius = 30f,
            ChargingState = ChargingState.Discharging
        }
    };

    /// <summary>모드 ON/OFF — OFF 시 대기 중인 확인은 취소 처리</summary>
    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        _logger.LogWarning("시뮬레이션 모드 {State}", enabled ? "ON — 하드웨어 동작을 웹 수동 확인으로 대체" : "OFF");
        if (!enabled)
        {
            lock (_lock)
            {
                _pending?.TrySetException(new OperationCanceledException("시뮬레이션 모드 해제됨"));
                _pending = null;
                PendingAction = null;
            }
        }
    }

    /// <summary>AMR 슬롯 가상 상태 설정 (slot 1~4)</summary>
    public void SetAmrSlot(int slot, bool occupied)
    {
        if (slot is >= 1 and <= 4)
        {
            AmrSlots[slot - 1] = occupied;
            _logger.LogInformation("[SIM] AMR 슬롯 {Slot} = {State}", slot, occupied ? "점유" : "빈");
        }
    }

    /// <summary>AMR 슬롯 점유 조회 (slot 1~4)</summary>
    public bool GetAmrSlot(int slot) => slot is >= 1 and <= 4 && AmrSlots[slot - 1];

    /// <summary>하드웨어 동작 대신 웹 확인을 기다린다. UI 의 '동작 완료' 클릭으로 해제.</summary>
    public async Task WaitForConfirmAsync(string action, CancellationToken ct)
    {
        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = tcs;
            PendingAction = action;
        }
        _logger.LogInformation("[SIM] 동작 대기: {Action} — 웹에서 '동작 완료' 클릭 필요", action);

        await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        try
        {
            await tcs.Task;
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_pending, tcs))
                {
                    _pending = null;
                    PendingAction = null;
                }
            }
        }
    }

    /// <summary>대기 중인 동작을 완료 처리 (웹 '동작 완료' 버튼)</summary>
    public bool ConfirmPending()
    {
        lock (_lock)
        {
            var ok = _pending?.TrySetResult(true) ?? false;
            if (ok) _logger.LogInformation("[SIM] 동작 완료 확인: {Action}", PendingAction);
            return ok;
        }
    }

    /// <summary>대기 중인 동작을 실패 처리 (웹 '실패 주입' 버튼) — 오류 경로 테스트용</summary>
    public bool FailPending(string? reason = null)
    {
        lock (_lock)
        {
            var msg = $"[SIM] 실패 주입: {PendingAction}" + (string.IsNullOrWhiteSpace(reason) ? "" : $" — {reason}");
            var ok = _pending?.TrySetException(new InvalidOperationException(msg)) ?? false;
            if (ok) _logger.LogWarning("{Message}", msg);
            return ok;
        }
    }

    /// <summary>
    /// Cobot DI 동작 완료 시 가상 슬롯 상태 자동 반영:
    /// DI0~3 AMR PICK(슬롯 비움) · DI4~7 AMR PLACE(슬롯 채움) ·
    /// DI12~13 자재포트 PLACE(채움) · DI14~15 자재포트 PICK(비움)
    /// </summary>
    public void ApplyCobotDiEffect(ushort di)
    {
        switch (di)
        {
            case <= 3:
                AmrSlots[di] = false;
                _logger.LogInformation("[SIM] AMR PICK → 슬롯 {Slot} 빈 상태로 변경", di + 1);
                break;
            case >= 4 and <= 7:
                AmrSlots[di - 4] = true;
                _logger.LogInformation("[SIM] AMR PLACE → 슬롯 {Slot} 점유 상태로 변경", di - 3);
                break;
            case 12:
                MaterialSlot1 = true;
                _logger.LogInformation("[SIM] 자재포트 PLACE → slot 1 점유");
                break;
            case 13:
                MaterialSlot2 = true;
                _logger.LogInformation("[SIM] 자재포트 PLACE → slot 2 점유");
                break;
            case 14:
                MaterialSlot1 = false;
                _logger.LogInformation("[SIM] 자재포트 PICK → slot 1 빈 상태");
                break;
            case 15:
                MaterialSlot2 = false;
                _logger.LogInformation("[SIM] 자재포트 PICK → slot 2 빈 상태");
                break;
        }
    }

    /// <summary>가상 자재포트 depth 감지 결과 (slot 1|2)</summary>
    public MagazineDetectionResult DetectMaterialSlot(int slot)
    {
        var detected = slot == 1 ? MaterialSlot1 : MaterialSlot2;
        return new MagazineDetectionResult
        {
            Detected = detected,
            ValidPixelRatio = detected ? 0.9 : 0.0,
            AverageDepthMm = detected ? (ushort)450 : (ushort)0,
            ValidDepthCoverage = 1.0,
            Reason = "시뮬레이션 가상 상태"
        };
    }
}
