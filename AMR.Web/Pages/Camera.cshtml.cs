using System.Text;
using AMR.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenCvSharp;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class CameraModel : PageModel
{
    private readonly CameraService _cameraService;
    private readonly MagazineDetectionService _magazineDetectionService;

    public CameraModel(CameraService cameraService, MagazineDetectionService magazineDetectionService)
    {
        _cameraService = cameraService;
        _magazineDetectionService = magazineDetectionService;
    }

    public bool IsConnected => _cameraService.IsConnected;

    public void OnGet() { }

    // ───── 매거진 감지 (depth 기반) 설정/테스트 ─────

    /// <summary>현재 매거진 감지 설정 + depth 프레임 해상도 반환</summary>
    public IActionResult OnGetMagazineSettings()
    {
        var (depthW, depthH) = _cameraService.GetDepthResolution();
        return new JsonResult(new
        {
            roiX = _magazineDetectionService.RoiX,
            roiY = _magazineDetectionService.RoiY,
            roiWidth = _magazineDetectionService.RoiWidth,
            roiHeight = _magazineDetectionService.RoiHeight,
            depthMinMm = _magazineDetectionService.DepthMinMm,
            depthMaxMm = _magazineDetectionService.DepthMaxMm,
            threshold = _magazineDetectionService.ValidPixelRatioThreshold,
            depthFrameWidth = depthW,
            depthFrameHeight = depthH
        });
    }

    public class MagazineSettingsRequest
    {
        public int RoiX { get; set; }
        public int RoiY { get; set; }
        public int RoiWidth { get; set; }
        public int RoiHeight { get; set; }
        public ushort DepthMinMm { get; set; }
        public ushort DepthMaxMm { get; set; }
        public double Threshold { get; set; }
    }

    /// <summary>설정 갱신</summary>
    public IActionResult OnPostUpdateMagazineSettings([FromBody] MagazineSettingsRequest req)
    {
        _magazineDetectionService.UpdateSettings(
            req.RoiX, req.RoiY, req.RoiWidth, req.RoiHeight,
            req.DepthMinMm, req.DepthMaxMm, req.Threshold);

        return new JsonResult(new
        {
            success = true,
            roiX = _magazineDetectionService.RoiX,
            roiY = _magazineDetectionService.RoiY,
            roiWidth = _magazineDetectionService.RoiWidth,
            roiHeight = _magazineDetectionService.RoiHeight,
            depthMinMm = _magazineDetectionService.DepthMinMm,
            depthMaxMm = _magazineDetectionService.DepthMaxMm,
            threshold = _magazineDetectionService.ValidPixelRatioThreshold
        });
    }

    /// <summary>현재 설정으로 즉시 1회 감지 — 결과 반환</summary>
    public IActionResult OnGetTestMagazineDetection()
    {
        var r = _magazineDetectionService.Detect();
        return new JsonResult(new
        {
            detected = r.Detected,
            validPixelRatio = r.ValidPixelRatio,
            inRangePixels = r.InRangePixels,
            totalPixels = r.TotalPixels,
            averageDepthMm = r.AverageDepthMm,
            validDepthCoverage = r.ValidDepthCoverage,
            reason = r.Reason
        });
    }

    public IActionResult OnGetStatus()
    {
        return new JsonResult(new
        {
            connected = _cameraService.IsConnected,
            hasDepth = _cameraService.HasDepthSupport
        });
    }

    public IActionResult OnGetEnumerateCameras()
    {
        var cameras = _cameraService.EnumerateCameras();
        return new JsonResult(cameras);
    }

    public IActionResult OnPostSwitchCamera(int deviceIndex, string backend)
    {
        var api = backend == "obsensor"
            ? (VideoCaptureAPIs)2600
            : VideoCaptureAPIs.ANY;
        _cameraService.SwitchCamera(deviceIndex, api);
        return new JsonResult(new { success = true });
    }

    public IActionResult OnGetCameraInfo()
    {
        return new JsonResult(new
        {
            deviceIndex = _cameraService.ActiveDeviceIndex,
            backend = _cameraService.ActiveBackend == (VideoCaptureAPIs)2600 ? "obsensor" : "any",
            hasDepth = _cameraService.HasDepthSupport,
            connected = _cameraService.IsConnected
        });
    }

    public IActionResult OnGetQrStatus()
    {
        var result = _cameraService.GetQrDetectionResult();
        return new JsonResult(new
        {
            result.Detected,
            result.DecodedText,
            result.CenterX,
            result.CenterY,
            result.RotationAngle,
            result.FrameCenterX,
            result.FrameCenterY,
            result.DeltaX,
            result.DeltaY,
            result.DepthMm,
            result.RealDeltaXMm,
            result.RealDeltaYMm,
            result.RealDistanceMm,
            DetectedAt = result.Detected ? result.DetectedAt.ToString("HH:mm:ss.fff") : ""
        });
    }

    public IActionResult OnPostSaveTeaching()
    {
        try
        {
            var teaching = _cameraService.SaveTeachingPosition();
            return new JsonResult(new
            {
                success = true,
                teaching.X,
                teaching.Y,
                teaching.DepthMm,
                teaching.Angle,
                teaching.QrText,
                TaughtAt = teaching.TaughtAt.ToString("HH:mm:ss.fff")
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public IActionResult OnPostClearTeaching()
    {
        _cameraService.ClearTeachingPosition();
        return new JsonResult(new { success = true });
    }

    public IActionResult OnGetTeachingStatus()
    {
        var teaching = _cameraService.GetTeachingPosition();
        var offset = _cameraService.GetPositionOffset();
        return new JsonResult(new
        {
            teaching = new
            {
                teaching.IsTaught,
                teaching.X,
                teaching.Y,
                teaching.DepthMm,
                teaching.Angle,
                teaching.QrText,
                TaughtAt = teaching.IsTaught ? teaching.TaughtAt.ToString("HH:mm:ss.fff") : ""
            },
            offset = new
            {
                offset.HasTeaching,
                offset.HasCurrent,
                offset.OffsetXMm,
                offset.OffsetYMm,
                offset.OffsetDepthMm,
                offset.OffsetAngle
            }
        });
    }

    public async Task<IActionResult> OnGetRgbStream()
    {
        await StreamFrames(() => _cameraService.GetCurrentRgbFrame());
        return new EmptyResult();
    }

    public async Task<IActionResult> OnGetDepthStream()
    {
        await StreamFrames(() => _cameraService.GetCurrentDepthFrame());
        return new EmptyResult();
    }

    private async Task StreamFrames(Func<byte[]> getFrame)
    {
        Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
        var token = HttpContext.RequestAborted;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var frame = getFrame();
                if (frame.Length > 0)
                {
                    var header = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n";
                    await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(header), token);
                    await Response.Body.WriteAsync(frame, token);
                    await Response.Body.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), token);
                    await Response.Body.FlushAsync(token);
                }

                await Task.Delay(50, token);
            }
        }
        catch (OperationCanceledException)
        {
            // 클라이언트 연결 종료 — 정상
        }
    }
}
