using AMR.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkiaSharp;
using ZXing;
using ZXing.SkiaSharp;

namespace AMR.Service;

public class CameraSettings
{
    public int DeviceIndex { get; set; }
    public int FrameWidth { get; set; } = 1280;
    public int FrameHeight { get; set; } = 720;
    public int DepthFrameWidth { get; set; } = 640;
    public int DepthFrameHeight { get; set; } = 480;
    public int TargetFps { get; set; } = 15;
    public int JpegQuality { get; set; } = 75;
    public int WarmupDelayMs { get; set; } = 500;

    /// <summary>Depth 카메라 X축 초점거리 (픽셀 단위, Orbbec 기본값 ~570)</summary>
    public double DepthFx { get; set; } = 570.0;
    /// <summary>Depth 카메라 Y축 초점거리 (픽셀 단위, Orbbec 기본값 ~570)</summary>
    public double DepthFy { get; set; } = 570.0;
}

public class CameraDeviceInfo
{
    public int Index { get; set; }
    public string Label { get; set; } = "";
}

public class CameraService : BackgroundService
{
    private readonly ILogger<CameraService> _logger;
    private readonly CameraSettings _settings;

    private const int CAP_OBSENSOR = 2600;

    private VideoCapture? _capture;
    private byte[] _currentRgbFrame = Array.Empty<byte>();
    private byte[] _currentDepthFrame = Array.Empty<byte>();
    private readonly object _rgbLock = new();
    private readonly object _depthLock = new();
    private readonly object _qrLock = new();
    private volatile bool _isConnected;

    private readonly BarcodeReader _qrReader = new()
    {
        Options = new ZXing.Common.DecodingOptions
        {
            PossibleFormats = [BarcodeFormat.QR_CODE],
            TryHarder = true
        }
    };
    private QrDetectionResult _lastQrResult = new();
    private QrTeachingPosition _teachingPosition = new();
    private readonly object _teachingLock = new();

    private CancellationTokenSource? _switchCts;
    private readonly object _switchLock = new();
    private volatile int _activeDeviceIndex;
    private volatile VideoCaptureAPIs _activeBackend = (VideoCaptureAPIs)CAP_OBSENSOR;
    private volatile bool _isEnumerating;

    public bool IsConnected => _isConnected;
    public int ActiveDeviceIndex => _activeDeviceIndex;
    public VideoCaptureAPIs ActiveBackend => _activeBackend;
    public bool HasDepthSupport => _activeBackend == (VideoCaptureAPIs)CAP_OBSENSOR;

    public CameraService(ILogger<CameraService> logger, CameraSettings settings)
    {
        _logger = logger;
        _settings = settings;
        _activeDeviceIndex = settings.DeviceIndex;
    }

    public byte[] GetCurrentRgbFrame()
    {
        lock (_rgbLock)
        {
            return _currentRgbFrame;
        }
    }

    public byte[] GetCurrentDepthFrame()
    {
        lock (_depthLock)
        {
            return _currentDepthFrame;
        }
    }

    public QrDetectionResult GetQrDetectionResult()
    {
        lock (_qrLock)
        {
            return _lastQrResult;
        }
    }

    /// <summary>현재 QR 감지 결과를 Teaching 위치로 저장</summary>
    public QrTeachingPosition SaveTeachingPosition()
    {
        var qr = GetQrDetectionResult();
        if (!qr.Detected || qr.DepthMm <= 0)
            throw new InvalidOperationException("유효한 QR 감지 결과가 없습니다. (Depth 포함 필요)");

        var teaching = new QrTeachingPosition
        {
            IsTaught = true,
            X = qr.RealDeltaXMm,
            Y = qr.RealDeltaYMm,
            DepthMm = qr.DepthMm,
            Angle = qr.RotationAngle,
            QrText = qr.DecodedText,
            TaughtAt = DateTime.Now
        };

        lock (_teachingLock)
        {
            _teachingPosition = teaching;
        }

        _logger.LogInformation("QR Teaching 저장: X={X:F1}mm, Y={Y:F1}mm, Depth={D:F0}mm, Angle={A:F1}°",
            teaching.X, teaching.Y, teaching.DepthMm, teaching.Angle);

        return teaching;
    }

    /// <summary>Teaching 위치 초기화</summary>
    public void ClearTeachingPosition()
    {
        lock (_teachingLock)
        {
            _teachingPosition = new QrTeachingPosition();
        }
    }

    /// <summary>저장된 Teaching 위치 조회</summary>
    public QrTeachingPosition GetTeachingPosition()
    {
        lock (_teachingLock)
        {
            return _teachingPosition;
        }
    }

    /// <summary>Teaching 위치 대비 현재 QR 위치의 변화량 계산 (Cobot 보정용)</summary>
    public QrPositionOffset GetPositionOffset()
    {
        var teaching = GetTeachingPosition();
        var qr = GetQrDetectionResult();

        var offset = new QrPositionOffset
        {
            HasTeaching = teaching.IsTaught,
            HasCurrent = qr.Detected && qr.DepthMm > 0
        };

        if (offset.HasTeaching && offset.HasCurrent)
        {
            offset.OffsetXMm = Math.Round(qr.RealDeltaXMm - teaching.X, 1);
            offset.OffsetYMm = Math.Round(qr.RealDeltaYMm - teaching.Y, 1);
            offset.OffsetDepthMm = Math.Round(qr.DepthMm - teaching.DepthMm, 1);
            offset.OffsetAngle = Math.Round(qr.RotationAngle - teaching.Angle, 1);
        }

        return offset;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CameraService 시작 (DeviceIndex: {Index})", _activeDeviceIndex);

        while (!stoppingToken.IsCancellationRequested)
        {
            _switchCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var loopToken = _switchCts.Token;
            try
            {
                await CaptureLoop(loopToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("카메라 전환 요청 (DeviceIndex: {Index}, Backend: {Backend})",
                    _activeDeviceIndex, _activeBackend);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "카메라 캡처 오류. 3초 후 재연결 시도...");
                await Task.Delay(3000, stoppingToken);
            }
            finally
            {
                _isConnected = false;
                ReleaseCapture();
                _switchCts?.Dispose();
                _switchCts = null;
            }
        }

        ReleaseCapture();
        _logger.LogInformation("CameraService 종료");
    }

    private async Task CaptureLoop(CancellationToken stoppingToken)
    {
        var deviceIndex = _activeDeviceIndex;
        var backend = _activeBackend;
        var isObsensor = backend == (VideoCaptureAPIs)CAP_OBSENSOR;

        _capture = new VideoCapture(deviceIndex, backend);

        if (!_capture.IsOpened())
        {
            _logger.LogWarning("카메라 열기 실패 (DeviceIndex: {Index}, Backend: {Backend})", deviceIndex, backend);
            _isConnected = false;
            ReleaseCapture();
            await Task.Delay(3000, stoppingToken);
            return;
        }

        _capture.Set(VideoCaptureProperties.FrameWidth, _settings.FrameWidth);
        _capture.Set(VideoCaptureProperties.FrameHeight, _settings.FrameHeight);

        _logger.LogInformation("카메라 열림 (DeviceIndex: {Index}, Backend: {Backend})", deviceIndex, backend);

        // 카메라 워밍업 대기
        await Task.Delay(_settings.WarmupDelayMs, stoppingToken);

        var delayMs = 1000 / _settings.TargetFps;
        var consecutiveFailures = 0;
        const int maxConsecutiveFailures = 10;
        var connected = false;

        using var rgbFrame = new Mat();
        using var depthFrame = new Mat();
        using var normalized = new Mat();
        using var colorized = new Mat();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_capture.Grab())
            {
                consecutiveFailures++;
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    _logger.LogWarning("프레임 Grab {Count}회 연속 실패. 카메라 연결 끊김.", maxConsecutiveFailures);
                    _isConnected = false;
                    break;
                }

                await Task.Delay(100, stoppingToken);
                continue;
            }

            bool hasRgb, hasDepth;
            if (isObsensor)
            {
                // OBSENSOR: channel 0 = depth map, channel 1 = BGR image
                hasDepth = _capture.Retrieve(depthFrame, 0) && !depthFrame.Empty();
                hasRgb = _capture.Retrieve(rgbFrame, 1) && !rgbFrame.Empty();
            }
            else
            {
                // 일반 USB 카메라: channel 0 = BGR image, depth 없음
                hasRgb = _capture.Retrieve(rgbFrame, 0) && !rgbFrame.Empty();
                hasDepth = false;
            }

            if (!hasDepth && !hasRgb)
            {
                consecutiveFailures++;
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    _logger.LogWarning("프레임 Retrieve {Count}회 연속 실패. 카메라 연결 끊김.", maxConsecutiveFailures);
                    _isConnected = false;
                    break;
                }

                await Task.Delay(100, stoppingToken);
                continue;
            }

            consecutiveFailures = 0;

            if (!connected)
            {
                _logger.LogInformation("카메라 연결 성공 (RGB: {RW}x{RH}, Depth: {DW}x{DH})",
                    hasRgb ? rgbFrame.Width : 0, hasRgb ? rgbFrame.Height : 0,
                    hasDepth ? depthFrame.Width : 0, hasDepth ? depthFrame.Height : 0);
                connected = true;
                _isConnected = true;
            }

            if (hasRgb)
            {
                DetectAndDrawQrCode(rgbFrame, hasDepth ? depthFrame : null);
                var rgbBuf = EncodeToJpeg(rgbFrame);
                lock (_rgbLock)
                {
                    _currentRgbFrame = rgbBuf;
                }
            }

            if (hasDepth)
            {
                var depthBuf = ColorizeAndEncodeDepth(depthFrame, normalized, colorized);
                lock (_depthLock)
                {
                    _currentDepthFrame = depthBuf;
                }
            }

            await Task.Delay(delayMs, stoppingToken);
        }
    }

    private void DetectAndDrawQrCode(Mat rgbFrame, Mat? depthFrame)
    {
        try
        {
            // 다단계 전처리로 각인 QR/저대비 QR 도 인식 시도 (첫 성공 시 즉시 리턴)
            var (result, methodUsed) = TryDecodeWithPreprocessing(rgbFrame);

            if (result?.Text is { Length: > 0 } decodedText && result.ResultPoints.Length >= 3)
            {
                var rp = result.ResultPoints;

                // ZXing ResultPoints: [bottomLeft, topLeft, topRight, (alignmentPattern - optional)]
                double p0X = rp[0].X, p0Y = rp[0].Y; // bottom-left
                double p1X = rp[1].X, p1Y = rp[1].Y; // top-left
                double p2X = rp[2].X, p2Y = rp[2].Y; // top-right

                // 4번째 점 (bottom-right) 계산: p0 + (p2 - p1)
                double p3X = p0X + (p2X - p1X), p3Y = p0Y + (p2Y - p1Y);

                // 중심 좌표 계산
                var centerX = (p0X + p1X + p2X + p3X) / 4.0;
                var centerY = (p0Y + p1Y + p2Y + p3Y) / 4.0;

                // 회전 각도 계산 (상단 변: topLeft→topRight 기준)
                var dx = p2X - p1X;
                var dy = p2Y - p1Y;
                var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

                // 카메라 프레임 중심
                var frameCenterX = rgbFrame.Width / 2.0;
                var frameCenterY = rgbFrame.Height / 2.0;

                // 카메라 센터 → QR 센터 델타 (픽셀)
                var deltaX = centerX - frameCenterX;
                var deltaY = centerY - frameCenterY;

                // Depth 기반 실제 거리 계산 (mm)
                double depthMm = 0;
                double realDeltaXMm = 0, realDeltaYMm = 0, realDistanceMm = 0;

                if (depthFrame is not null && !depthFrame.Empty())
                {
                    // RGB 해상도 → Depth 해상도로 QR 중심 좌표 스케일링
                    var scaleX = (double)depthFrame.Width / rgbFrame.Width;
                    var scaleY = (double)depthFrame.Height / rgbFrame.Height;
                    var depthPixelX = (int)(centerX * scaleX);
                    var depthPixelY = (int)(centerY * scaleY);

                    // 범위 체크
                    depthPixelX = Math.Clamp(depthPixelX, 0, depthFrame.Width - 1);
                    depthPixelY = Math.Clamp(depthPixelY, 0, depthFrame.Height - 1);

                    // 16-bit depth 값 읽기 (Orbbec: mm 단위)
                    depthMm = depthFrame.At<ushort>(depthPixelY, depthPixelX);

                    // depth가 유효한 경우 (0 = 측정 불가) 실제 거리 계산
                    if (depthMm > 0)
                    {
                        // depth 이미지 좌표계에서의 delta (픽셀)
                        var depthCx = depthFrame.Width / 2.0;
                        var depthCy = depthFrame.Height / 2.0;
                        var depthDeltaX = depthPixelX - depthCx;
                        var depthDeltaY = depthPixelY - depthCy;

                        // 핀홀 카메라 모델: real = pixel_delta * depth / focal_length
                        realDeltaXMm = depthDeltaX * depthMm / _settings.DepthFx;
                        realDeltaYMm = depthDeltaY * depthMm / _settings.DepthFy;
                        realDistanceMm = Math.Sqrt(realDeltaXMm * realDeltaXMm
                                                 + realDeltaYMm * realDeltaYMm
                                                 + depthMm * depthMm);
                    }
                }

                // QR 결과 저장
                lock (_qrLock)
                {
                    _lastQrResult = new QrDetectionResult
                    {
                        Detected = true,
                        DecodedText = decodedText,
                        CenterX = Math.Round(centerX, 1),
                        CenterY = Math.Round(centerY, 1),
                        RotationAngle = Math.Round(angle, 1),
                        FrameCenterX = Math.Round(frameCenterX, 1),
                        FrameCenterY = Math.Round(frameCenterY, 1),
                        DeltaX = Math.Round(deltaX, 1),
                        DeltaY = Math.Round(deltaY, 1),
                        DepthMm = Math.Round(depthMm, 1),
                        RealDeltaXMm = Math.Round(realDeltaXMm, 1),
                        RealDeltaYMm = Math.Round(realDeltaYMm, 1),
                        RealDistanceMm = Math.Round(realDistanceMm, 1),
                        DetectedAt = DateTime.Now
                    };
                }

                // QR코드 경계 그리기 (초록색)
                var pts = new[]
                {
                    new Point((int)p0X, (int)p0Y), new Point((int)p1X, (int)p1Y),
                    new Point((int)p2X, (int)p2Y), new Point((int)p3X, (int)p3Y)
                };
                Cv2.Polylines(rgbFrame, new[] { pts }, true, new Scalar(0, 255, 0), 2);

                // 중심점 마커 (빨간색 십자)
                var center = new Point((int)centerX, (int)centerY);
                Cv2.DrawMarker(rgbFrame, center, new Scalar(0, 0, 255),
                    MarkerTypes.Cross, 20, 2);

                // 카메라 중심점 마커 (파란색 십자)
                var frameCenter = new Point((int)frameCenterX, (int)frameCenterY);
                Cv2.DrawMarker(rgbFrame, frameCenter, new Scalar(255, 0, 0),
                    MarkerTypes.Cross, 15, 1);

                // 카메라 중심 → QR 중심 연결선 (노란색)
                Cv2.Line(rgbFrame, frameCenter, center, new Scalar(0, 255, 255), 1, LineTypes.Link4);

                // 텍스트 오버레이
                var label = methodUsed == "Original"
                    ? $"QR: {decodedText}"
                    : $"QR: {decodedText} [{methodUsed}]";
                var coordLabel = $"Center: ({centerX:F1}, {centerY:F1})  Angle: {angle:F1} deg";
                var deltaLabel = $"Delta: ({deltaX:F1}, {deltaY:F1}) px";
                var depthLabel = depthMm > 0
                    ? $"Depth: {depthMm:F0}mm  Real: ({realDeltaXMm:F1}, {realDeltaYMm:F1}) mm  Dist: {realDistanceMm:F1}mm"
                    : "Depth: N/A";

                Cv2.Rectangle(rgbFrame, new Point(5, 5), new Point(640, 110),
                    new Scalar(0, 0, 0), -1);
                Cv2.PutText(rgbFrame, label, new Point(10, 25),
                    HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);
                Cv2.PutText(rgbFrame, coordLabel, new Point(10, 50),
                    HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 255), 2);
                Cv2.PutText(rgbFrame, deltaLabel, new Point(10, 75),
                    HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 200, 0), 2);
                Cv2.PutText(rgbFrame, depthLabel, new Point(10, 100),
                    HersheyFonts.HersheySimplex, 0.55, new Scalar(100, 255, 100), 2);
            }
            else
            {
                lock (_qrLock)
                {
                    _lastQrResult.Detected = false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "QR코드 감지 중 오류");
        }
    }

    /// <summary>
    /// 다단계 전처리로 QR 디코딩 시도 — 각인 QR / 저대비 / 조명 불균일 대응.
    /// 첫 성공 시 즉시 리턴. 어떤 전처리로 인식했는지 함께 반환.
    /// </summary>
    private (ZXing.Result? result, string methodUsed) TryDecodeWithPreprocessing(Mat rgbFrame)
    {
        // 1. 원본 BGR — 정상 흰배경/검정 QR
        using (var bgra = new Mat())
        {
            Cv2.CvtColor(rgbFrame, bgra, ColorConversionCodes.BGR2BGRA);
            var r = DecodeBgra(bgra);
            if (r != null) return (r, "Original");
        }

        using var gray = new Mat();
        Cv2.CvtColor(rgbFrame, gray, ColorConversionCodes.BGR2GRAY);

        // 2. Otsu binary (반전) — 각인이 어둡게 보이는 경우 (이번 케이스)
        if (TryDecodeFromGray(gray, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu, out var rOtsuInv))
            return (rOtsuInv, "Otsu-Inverted");

        // 3. Otsu binary — 각인이 밝게 보이는 경우
        if (TryDecodeFromGray(gray, ThresholdTypes.Binary | ThresholdTypes.Otsu, out var rOtsu))
            return (rOtsu, "Otsu");

        // 4. Adaptive threshold (Gaussian) — 조명 불균일
        using (var adaptive = new Mat())
        using (var bgra = new Mat())
        {
            Cv2.AdaptiveThreshold(gray, adaptive, 255, AdaptiveThresholdTypes.GaussianC,
                                  ThresholdTypes.BinaryInv, 31, 5);
            Cv2.CvtColor(adaptive, bgra, ColorConversionCodes.GRAY2BGRA);
            var r = DecodeBgra(bgra);
            if (r != null) return (r, "AdaptiveGaussian");
        }

        // 5. CLAHE 명암 향상 + Otsu 반전 — 저대비 영상 보정
        using (var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8)))
        using (var enhanced = new Mat())
        {
            clahe.Apply(gray, enhanced);
            if (TryDecodeFromGray(enhanced, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu, out var rClahe))
                return (rClahe, "CLAHE+Otsu");
        }

        return (null, "");
    }

    /// <summary>그레이 Mat 에 임계값 적용 후 ZXing 디코드</summary>
    private bool TryDecodeFromGray(Mat gray, ThresholdTypes thresh, out ZXing.Result? result)
    {
        using var bin = new Mat();
        Cv2.Threshold(gray, bin, 0, 255, thresh);
        using var bgra = new Mat();
        Cv2.CvtColor(bin, bgra, ColorConversionCodes.GRAY2BGRA);
        result = DecodeBgra(bgra);
        return result != null;
    }

    /// <summary>BGRA Mat 을 SKBitmap 으로 감싸서 ZXing 에 입력</summary>
    private ZXing.Result? DecodeBgra(Mat bgra)
    {
        var info = new SKImageInfo(bgra.Width, bgra.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap();
        bitmap.InstallPixels(info, bgra.Data, bgra.Width * 4);
        return _qrReader.Decode(bitmap);
    }

    private byte[] ColorizeAndEncodeDepth(Mat depthFrame, Mat normalized, Mat colorized)
    {
        // 16-bit depth → 8-bit 정규화
        depthFrame.ConvertTo(normalized, MatType.CV_8U, 255.0 / 10000.0);

        // 컬러맵 적용 (TURBO: 직관적인 depth 시각화)
        Cv2.ApplyColorMap(normalized, colorized, ColormapTypes.Turbo);

        return EncodeToJpeg(colorized);
    }

    private byte[] EncodeToJpeg(Mat frame)
    {
        var prms = new[] { new ImageEncodingParam(ImwriteFlags.JpegQuality, _settings.JpegQuality) };
        Cv2.ImEncode(".jpg", frame, out var buf, prms);
        return buf;
    }

    private void ReleaseCapture()
    {
        if (_capture is not null)
        {
            _capture.Release();
            _capture.Dispose();
            _capture = null;
        }

        lock (_rgbLock)
        {
            _currentRgbFrame = Array.Empty<byte>();
        }

        lock (_depthLock)
        {
            _currentDepthFrame = Array.Empty<byte>();
        }
    }

    public void SwitchCamera(int deviceIndex, VideoCaptureAPIs backend)
    {
        lock (_switchLock)
        {
            _activeDeviceIndex = deviceIndex;
            _activeBackend = backend;
            _switchCts?.Cancel();
        }
    }

    public List<CameraDeviceInfo> EnumerateCameras()
    {
        if (_isEnumerating)
            return [];

        _isEnumerating = true;
        try
        {
            var results = new List<CameraDeviceInfo>();
            for (var i = 0; i <= 9; i++)
            {
                try
                {
                    using var cap = new VideoCapture(i, VideoCaptureAPIs.ANY);
                    if (cap.IsOpened())
                    {
                        var w = (int)cap.Get(VideoCaptureProperties.FrameWidth);
                        var h = (int)cap.Get(VideoCaptureProperties.FrameHeight);
                        results.Add(new CameraDeviceInfo { Index = i, Label = $"Camera {i} ({w}x{h})" });
                        cap.Release();
                    }
                }
                catch
                {
                    // skip unavailable device
                }
            }

            return results;
        }
        finally
        {
            _isEnumerating = false;
        }
    }

    public override void Dispose()
    {
        ReleaseCapture();
        base.Dispose();
    }
}
