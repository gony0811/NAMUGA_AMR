using System.Text.Json;
using AMR.Models;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 자재포트 / 설비포트의 매거진 존재 여부를 depth 카메라로 판정하기 위한 설정 보유 + 진입점.
///
/// 동작:
///   - CameraService 의 최신 depth frame 의 ROI 영역에서 픽셀별 depth 값을 검사
///   - 픽셀의 depth 가 [DepthMinMm, DepthMaxMm] 범위에 든 비율 ≥ ValidPixelRatioThreshold 면 매거진 있음
///
/// 설정 영구 저장:
///   - 시작 시 magazine_detection.json (exe 와 같은 폴더) 에서 로드
///   - UpdateSettings() 호출 시 즉시 같은 파일에 저장
///   - 파일 없으면 코드의 기본값 사용 + 첫 저장 시 자동 생성
///
/// Web UI `/Camera` 페이지에서 조정 가능.
/// </summary>
public class MagazineDetectionService
{
    private readonly CameraService _cameraService;
    private readonly ILogger<MagazineDetectionService> _logger;
    private readonly object _lock = new();
    private readonly string _settingsFilePath;

    /// <summary>ROI 좌상단 X (depth frame 픽셀 좌표 기준, 보통 640x480)</summary>
    public int RoiX { get; set; } = 220;

    /// <summary>ROI 좌상단 Y</summary>
    public int RoiY { get; set; } = 180;

    /// <summary>ROI 가로 폭(px)</summary>
    public int RoiWidth { get; set; } = 200;

    /// <summary>ROI 세로 높이(px)</summary>
    public int RoiHeight { get; set; } = 120;

    /// <summary>매거진 표면으로 인정할 depth 하한 (mm). 카메라~매거진 윗면 거리.</summary>
    public ushort DepthMinMm { get; set; } = 300;

    /// <summary>매거진 표면으로 인정할 depth 상한 (mm).</summary>
    public ushort DepthMaxMm { get; set; } = 600;

    /// <summary>매거진 있음 판정용 임계 비율 (0.0~1.0). 예: 0.4 → ROI 픽셀의 40% 이상이 범위면 있음.</summary>
    public double ValidPixelRatioThreshold { get; set; } = 0.4;

    public MagazineDetectionService(CameraService cameraService, ILogger<MagazineDetectionService> logger)
    {
        _cameraService = cameraService;
        _logger = logger;

        // exe (또는 dll) 와 같은 폴더에 저장 — amr.db 와 동일 위치 정책
        _settingsFilePath = Path.Combine(AppContext.BaseDirectory, "magazine_detection.json");

        LoadFromFile();
    }

    /// <summary>현재 설정으로 매거진 감지 1회 수행</summary>
    public MagazineDetectionResult Detect()
    {
        int x, y, w, h;
        ushort dmin, dmax;
        double thresh;
        lock (_lock)
        {
            x = RoiX; y = RoiY; w = RoiWidth; h = RoiHeight;
            dmin = DepthMinMm; dmax = DepthMaxMm;
            thresh = ValidPixelRatioThreshold;
        }

        var result = _cameraService.DetectMagazineInRoi(x, y, w, h, dmin, dmax, thresh);

        _logger.LogDebug(
            "MagazineDetect: Detected={Detected}, Ratio={Ratio:P1}, AvgDepth={Avg}mm, Coverage={Cov:P0} — {Reason}",
            result.Detected, result.ValidPixelRatio, result.AverageDepthMm, result.ValidDepthCoverage, result.Reason);

        return result;
    }

    /// <summary>설정 일괄 갱신 (UI POST 핸들러에서 호출) — 즉시 파일 저장</summary>
    public void UpdateSettings(
        int roiX, int roiY, int roiWidth, int roiHeight,
        ushort depthMinMm, ushort depthMaxMm, double threshold)
    {
        lock (_lock)
        {
            RoiX = Math.Max(0, roiX);
            RoiY = Math.Max(0, roiY);
            RoiWidth = Math.Max(1, roiWidth);
            RoiHeight = Math.Max(1, roiHeight);
            DepthMinMm = depthMinMm;
            DepthMaxMm = depthMaxMm > depthMinMm ? depthMaxMm : (ushort)(depthMinMm + 1);
            ValidPixelRatioThreshold = Math.Clamp(threshold, 0.0, 1.0);
        }

        _logger.LogInformation(
            "MagazineDetect 설정 갱신: ROI=({X},{Y},{W},{H}), Depth=[{Min}~{Max}]mm, Threshold={Thresh:P0}",
            RoiX, RoiY, RoiWidth, RoiHeight, DepthMinMm, DepthMaxMm, ValidPixelRatioThreshold);

        SaveToFile();
    }

    #region 영구 저장 / 로드

    /// <summary>JSON 직렬화용 — 파일 포맷 변경 영향 최소화 위해 별도 record</summary>
    private record PersistedSettings(
        int RoiX, int RoiY, int RoiWidth, int RoiHeight,
        ushort DepthMinMm, ushort DepthMaxMm,
        double ValidPixelRatioThreshold);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true   // 사람이 직접 열어서 편집 가능하도록
    };

    private void LoadFromFile()
    {
        if (!File.Exists(_settingsFilePath))
        {
            _logger.LogInformation(
                "MagazineDetect 설정 파일 없음 — 코드 기본값 사용 (다음 저장 시 자동 생성): {Path}",
                _settingsFilePath);
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            var data = JsonSerializer.Deserialize<PersistedSettings>(json, JsonOptions);
            if (data is null)
            {
                _logger.LogWarning("MagazineDetect 설정 파일 파싱 결과 null — 기본값 사용");
                return;
            }

            lock (_lock)
            {
                RoiX = Math.Max(0, data.RoiX);
                RoiY = Math.Max(0, data.RoiY);
                RoiWidth = Math.Max(1, data.RoiWidth);
                RoiHeight = Math.Max(1, data.RoiHeight);
                DepthMinMm = data.DepthMinMm;
                DepthMaxMm = data.DepthMaxMm > data.DepthMinMm ? data.DepthMaxMm : (ushort)(data.DepthMinMm + 1);
                ValidPixelRatioThreshold = Math.Clamp(data.ValidPixelRatioThreshold, 0.0, 1.0);
            }

            _logger.LogInformation(
                "MagazineDetect 설정 로드 완료: ROI=({X},{Y},{W},{H}), Depth=[{Min}~{Max}]mm, Threshold={Thresh:P0} ({Path})",
                RoiX, RoiY, RoiWidth, RoiHeight, DepthMinMm, DepthMaxMm, ValidPixelRatioThreshold, _settingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MagazineDetect 설정 로드 실패 — 기본값 사용: {Path}", _settingsFilePath);
        }
    }

    private void SaveToFile()
    {
        try
        {
            PersistedSettings data;
            lock (_lock)
            {
                data = new PersistedSettings(
                    RoiX, RoiY, RoiWidth, RoiHeight,
                    DepthMinMm, DepthMaxMm, ValidPixelRatioThreshold);
            }

            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);

            _logger.LogInformation("MagazineDetect 설정 파일 저장 완료: {Path}", _settingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MagazineDetect 설정 파일 저장 실패: {Path}", _settingsFilePath);
        }
    }

    #endregion
}
