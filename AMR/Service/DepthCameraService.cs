using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkiaSharp;

namespace AMR.Service;

public class DepthCameraSettings
{
    public int DeviceIndex { get; set; }
    public int FrameWidth { get; set; } = 640;
    public int FrameHeight { get; set; } = 480;
    public int TargetFps { get; set; } = 15;
    public int JpegQuality { get; set; } = 75;
    public int WarmupDelayMs { get; set; } = 500;
}

public class DepthCameraService : BackgroundService
{
    private readonly ILogger<DepthCameraService> _logger;
    private readonly DepthCameraSettings _settings;

    private VideoCapture? _capture;
    private byte[] _currentFrame = Array.Empty<byte>();
    private readonly object _frameLock = new();
    private volatile bool _isConnected;

    public bool IsConnected => _isConnected;

    public DepthCameraService(ILogger<DepthCameraService> logger, DepthCameraSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public byte[] GetCurrentFrame()
    {
        lock (_frameLock)
        {
            return _currentFrame;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DepthCameraService 시작 (DeviceIndex: {Index})", _settings.DeviceIndex);

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
                _logger.LogWarning(ex, "Depth 카메라 캡처 오류. 3초 후 재연결 시도...");
                _isConnected = false;
                ReleaseCapture();
                await Task.Delay(3000, stoppingToken);
            }
        }

        ReleaseCapture();
        _logger.LogInformation("DepthCameraService 종료");
    }

    private async Task CaptureLoop(CancellationToken stoppingToken)
    {
        // OBSENSOR 백엔드(2600)로 Orbbec depth 카메라 접근
        const int CAP_OBSENSOR = 2600;
        _capture = new VideoCapture(_settings.DeviceIndex, (VideoCaptureAPIs)CAP_OBSENSOR);

        if (!_capture.IsOpened())
        {
            _logger.LogWarning("Depth 카메라 열기 실패 (DeviceIndex: {Index}, Backend: OBSENSOR)", _settings.DeviceIndex);
            _isConnected = false;
            ReleaseCapture();
            await Task.Delay(3000, stoppingToken);
            return;
        }

        _capture.Set(VideoCaptureProperties.FrameWidth, _settings.FrameWidth);
        _capture.Set(VideoCaptureProperties.FrameHeight, _settings.FrameHeight);

        var actualWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
        var actualHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
        _logger.LogInformation(
            "Depth 카메라 열림 (DeviceIndex: {Index}, 요청: {RW}x{RH}, 실제: {AW}x{AH})",
            _settings.DeviceIndex, _settings.FrameWidth, _settings.FrameHeight,
            actualWidth, actualHeight);

        // 카메라 워밍업 대기
        await Task.Delay(_settings.WarmupDelayMs, stoppingToken);

        var delayMs = 1000 / _settings.TargetFps;
        var consecutiveFailures = 0;
        const int maxConsecutiveFailures = 10;
        var connected = false;

        using var depthFrame = new Mat();
        using var normalized = new Mat();
        using var colorized = new Mat();

        while (!stoppingToken.IsCancellationRequested)
        {
            // Grab + Retrieve 패턴으로 depth 프레임 획득
            if (!_capture.Grab())
            {
                consecutiveFailures++;
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    _logger.LogWarning("Depth 프레임 Grab {Count}회 연속 실패. 카메라 연결 끊김.", maxConsecutiveFailures);
                    _isConnected = false;
                    break;
                }

                await Task.Delay(100, stoppingToken);
                continue;
            }

            // CAP_OBSENSOR_DEPTH_MAP (channel 0) 로 depth 데이터 가져오기
            if (!_capture.Retrieve(depthFrame, 0) || depthFrame.Empty())
            {
                consecutiveFailures++;
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    _logger.LogWarning("Depth 프레임 Retrieve {Count}회 연속 실패. 카메라 연결 끊김.", maxConsecutiveFailures);
                    _isConnected = false;
                    break;
                }

                await Task.Delay(100, stoppingToken);
                continue;
            }

            consecutiveFailures = 0;

            if (!connected)
            {
                _logger.LogInformation("Depth 카메라 연결 성공 (프레임 크기: {W}x{H}, Type: {Type})",
                    depthFrame.Width, depthFrame.Height, depthFrame.Type());
                connected = true;
                _isConnected = true;
            }

            // Depth 데이터 → 컬러 depth map 변환
            var buf = ColorizeAndEncode(depthFrame, normalized, colorized);
            lock (_frameLock)
            {
                _currentFrame = buf;
            }

            await Task.Delay(delayMs, stoppingToken);
        }
    }

    private byte[] ColorizeAndEncode(Mat depthFrame, Mat normalized, Mat colorized)
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

        lock (_frameLock)
        {
            _currentFrame = Array.Empty<byte>();
        }
    }

    public override void Dispose()
    {
        ReleaseCapture();
        base.Dispose();
    }
}
