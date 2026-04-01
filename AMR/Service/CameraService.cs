using AMR.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkiaSharp;

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
}

public class CameraService : BackgroundService
{
    private readonly ILogger<CameraService> _logger;
    private readonly CameraSettings _settings;

    private VideoCapture? _capture;
    private byte[] _currentRgbFrame = Array.Empty<byte>();
    private byte[] _currentDepthFrame = Array.Empty<byte>();
    private readonly object _rgbLock = new();
    private readonly object _depthLock = new();
    private readonly object _qrLock = new();
    private volatile bool _isConnected;

    private readonly QRCodeDetector _qrDetector = new();
    private QrDetectionResult _lastQrResult = new();

    public bool IsConnected => _isConnected;

    public CameraService(ILogger<CameraService> logger, CameraSettings settings)
    {
        _logger = logger;
        _settings = settings;
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CameraService 시작 (DeviceIndex: {Index})", _settings.DeviceIndex);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CaptureLoop(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "카메라 캡처 오류. 3초 후 재연결 시도...");
                _isConnected = false;
                ReleaseCapture();
                await Task.Delay(3000, stoppingToken);
            }
        }

        ReleaseCapture();
        _logger.LogInformation("CameraService 종료");
    }

    private async Task CaptureLoop(CancellationToken stoppingToken)
    {
        // OBSENSOR 백엔드(2600)로 Orbbec 카메라 접근
        const int CAP_OBSENSOR = 2600;
        _capture = new VideoCapture(_settings.DeviceIndex, (VideoCaptureAPIs)CAP_OBSENSOR);

        if (!_capture.IsOpened())
        {
            _logger.LogWarning("카메라 열기 실패 (DeviceIndex: {Index})", _settings.DeviceIndex);
            _isConnected = false;
            ReleaseCapture();
            await Task.Delay(3000, stoppingToken);
            return;
        }

        _capture.Set(VideoCaptureProperties.FrameWidth, _settings.FrameWidth);
        _capture.Set(VideoCaptureProperties.FrameHeight, _settings.FrameHeight);

        _logger.LogInformation("카메라 열림 (DeviceIndex: {Index})", _settings.DeviceIndex);

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

            // channel 0: depth map, channel 1: BGR image
            var hasDepth = _capture.Retrieve(depthFrame, 0) && !depthFrame.Empty();
            var hasRgb = _capture.Retrieve(rgbFrame, 1) && !rgbFrame.Empty();

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
                DetectAndDrawQrCode(rgbFrame);
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

    private void DetectAndDrawQrCode(Mat rgbFrame)
    {
        try
        {
            var decoded = _qrDetector.DetectAndDecode(rgbFrame, out var points);

            if (!string.IsNullOrEmpty(decoded) && points.Length >= 4)
            {
                // 중심 좌표 계산 (4개 꼭짓점 평균)
                var centerX = (points[0].X + points[1].X + points[2].X + points[3].X) / 4.0;
                var centerY = (points[0].Y + points[1].Y + points[2].Y + points[3].Y) / 4.0;

                // 회전 각도 계산 (상단 변: points[0]→points[1] 기준)
                var dx = points[1].X - points[0].X;
                var dy = points[1].Y - points[0].Y;
                var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

                // 카메라 프레임 중심
                var frameCenterX = rgbFrame.Width / 2.0;
                var frameCenterY = rgbFrame.Height / 2.0;

                // 카메라 센터 → QR 센터 델타 (픽셀)
                var deltaX = centerX - frameCenterX;
                var deltaY = centerY - frameCenterY;

                // QR 결과 저장
                lock (_qrLock)
                {
                    _lastQrResult = new QrDetectionResult
                    {
                        Detected = true,
                        DecodedText = decoded,
                        CenterX = Math.Round(centerX, 1),
                        CenterY = Math.Round(centerY, 1),
                        RotationAngle = Math.Round(angle, 1),
                        FrameCenterX = Math.Round(frameCenterX, 1),
                        FrameCenterY = Math.Round(frameCenterY, 1),
                        DeltaX = Math.Round(deltaX, 1),
                        DeltaY = Math.Round(deltaY, 1),
                        DetectedAt = DateTime.Now
                    };
                }

                // QR코드 경계 그리기 (초록색)
                var pts = points.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                Cv2.Polylines(rgbFrame, new[] { pts }, true, new Scalar(0, 255, 0), 2);

                // 중심점 마커 (빨간색 십자)
                var center = new Point((int)centerX, (int)centerY);
                Cv2.DrawMarker(rgbFrame, center, new Scalar(0, 0, 255),
                    MarkerTypes.Cross, 20, 2);

                // 카메라 중심점 마커 (파란색 십자)
                var frameCenter = new Point((int)frameCenterX, (int)frameCenterY);
                Cv2.DrawMarker(rgbFrame, frameCenter, new Scalar(255, 0, 0),
                    MarkerTypes.Cross, 15, 1);

                // 카메라 중심 → QR 중심 연결선 (노란색 점선)
                Cv2.Line(rgbFrame, frameCenter, center, new Scalar(0, 255, 255), 1, LineTypes.Link4);

                // 텍스트 오버레이
                var label = $"QR: {decoded}";
                var coordLabel = $"Center: ({centerX:F1}, {centerY:F1})  Angle: {angle:F1} deg";
                var deltaLabel = $"Delta: ({deltaX:F1}, {deltaY:F1})";

                Cv2.Rectangle(rgbFrame, new Point(5, 5), new Point(560, 85),
                    new Scalar(0, 0, 0), -1);
                Cv2.PutText(rgbFrame, label, new Point(10, 25),
                    HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);
                Cv2.PutText(rgbFrame, coordLabel, new Point(10, 50),
                    HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 255), 2);
                Cv2.PutText(rgbFrame, deltaLabel, new Point(10, 75),
                    HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 200, 0), 2);
            }
            else
            {
                lock (_qrLock)
                {
                    if (_lastQrResult.Detected)
                    {
                        _lastQrResult = new QrDetectionResult();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "QR코드 감지 중 오류");
        }
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
        // BGR -> BGRA 변환 후 SkiaSharp로 JPEG 인코딩
        using var bgraFrame = new Mat();
        Cv2.CvtColor(frame, bgraFrame, ColorConversionCodes.BGR2BGRA);

        var info = new SKImageInfo(bgraFrame.Width, bgraFrame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var rowBytes = bgraFrame.Width * 4;

        using var bitmap = new SKBitmap();
        bitmap.InstallPixels(info, bgraFrame.Data, rowBytes);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, _settings.JpegQuality);
        return data.ToArray();
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

    public override void Dispose()
    {
        ReleaseCapture();
        base.Dispose();
    }
}
