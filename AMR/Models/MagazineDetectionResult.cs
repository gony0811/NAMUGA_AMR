namespace AMR.Models;

/// <summary>
/// Depth 카메라 ROI 내 매거진 존재 여부 감지 결과.
/// </summary>
public record MagazineDetectionResult
{
    /// <summary>최종 판정 — 매거진 있음(true) / 없음 또는 판단 불가(false)</summary>
    public bool Detected { get; init; }

    /// <summary>ROI 내 전체 픽셀 중 [DepthMinMm, DepthMaxMm] 범위에 든 비율 (0.0~1.0)</summary>
    public double ValidPixelRatio { get; init; }

    /// <summary>ROI 내 범위 안에 든 픽셀 수</summary>
    public int InRangePixels { get; init; }

    /// <summary>ROI 전체 픽셀 수</summary>
    public int TotalPixels { get; init; }

    /// <summary>ROI 내 평균 depth (mm) — 0 픽셀(invalid) 제외</summary>
    public ushort AverageDepthMm { get; init; }

    /// <summary>ROI 내 0이 아닌 픽셀 비율 — 너무 낮으면 ROI 가 카메라 시야 밖이거나 노이즈</summary>
    public double ValidDepthCoverage { get; init; }

    /// <summary>판정 부가 정보 / 실패 사유</summary>
    public string Reason { get; init; } = "";
}
