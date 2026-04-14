namespace AMR.Models;

public class QrDetectionResult
{
    public bool Detected { get; set; }
    public string DecodedText { get; set; } = string.Empty;
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double RotationAngle { get; set; }
    public double FrameCenterX { get; set; }
    public double FrameCenterY { get; set; }
    public double DeltaX { get; set; }
    public double DeltaY { get; set; }

    /// <summary>QR 중심의 Depth 거리 (mm)</summary>
    public double DepthMm { get; set; }
    /// <summary>카메라 중심 → QR 중심 실제 X 거리 (mm)</summary>
    public double RealDeltaXMm { get; set; }
    /// <summary>카메라 중심 → QR 중심 실제 Y 거리 (mm)</summary>
    public double RealDeltaYMm { get; set; }
    /// <summary>카메라 중심 → QR 중심 3D 직선 거리 (mm)</summary>
    public double RealDistanceMm { get; set; }

    public DateTime DetectedAt { get; set; }
}
