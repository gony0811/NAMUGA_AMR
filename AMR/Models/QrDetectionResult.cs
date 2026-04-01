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
    public DateTime DetectedAt { get; set; }
}
