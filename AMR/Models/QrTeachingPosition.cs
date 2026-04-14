namespace AMR.Models;

/// <summary>
/// QR코드 초기 Teaching 위치 — AMR 위치 변화량 계산의 기준점
/// </summary>
public class QrTeachingPosition
{
    public bool IsTaught { get; set; }

    /// <summary>Teaching 시점의 실제 X 위치 (mm)</summary>
    public double X { get; set; }
    /// <summary>Teaching 시점의 실제 Y 위치 (mm)</summary>
    public double Y { get; set; }
    /// <summary>Teaching 시점의 Depth (mm)</summary>
    public double DepthMm { get; set; }
    /// <summary>Teaching 시점의 회전 각도 (deg)</summary>
    public double Angle { get; set; }
    /// <summary>Teaching 시점의 QR 텍스트</summary>
    public string QrText { get; set; } = string.Empty;
    /// <summary>Teaching 시점</summary>
    public DateTime TaughtAt { get; set; }
}

/// <summary>
/// Teaching 위치 대비 현재 위치 변화량 — Cobot 이동 보정에 사용
/// </summary>
public class QrPositionOffset
{
    public bool HasTeaching { get; set; }
    public bool HasCurrent { get; set; }

    /// <summary>X 변화량 (mm): 현재 - Teaching</summary>
    public double OffsetXMm { get; set; }
    /// <summary>Y 변화량 (mm): 현재 - Teaching</summary>
    public double OffsetYMm { get; set; }
    /// <summary>Depth 변화량 (mm): 현재 - Teaching</summary>
    public double OffsetDepthMm { get; set; }
    /// <summary>각도 변화량 (deg): 현재 - Teaching</summary>
    public double OffsetAngle { get; set; }
}
