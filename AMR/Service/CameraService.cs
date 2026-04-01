using System.Runtime.InteropServices;
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
    public int TargetFps { get; set; } = 20;
    public int JpegQuality { get; set; } = 75;
    public int WarmupDelayMs { get; set; } = 500;
}

public class CameraService : BackgroundService
{
    private readonly ILogger<CameraService> _logger;
    private readonly CameraSettings _settings;

    private VideoCapture? _capture;
    private byte[] _currentFrame = Array.Empty<byte>();
    private readonly object _frameLock = new();
    private volatile bool _isConnected;

    public bool IsConnected => _isConnected;

    public CameraService(ILogger<CameraService> logger, CameraSettings settings)
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
        _capture = new VideoCapture(_settings.DeviceIndex, VideoCaptureAPIs.AVFOUNDATION);

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

        var actualWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
        var actualHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
        _logger.LogInformation(
            "카메라 열림 (DeviceIndex: {Index}, 요청: {RW}x{RH}, 실제: {AW}x{AH})",
            _settings.DeviceIndex, _settings.FrameWidth, _settings.FrameHeight,
            actualWidth, actualHeight);

        // 카메라 워밍업 대기
        await Task.Delay(_settings.WarmupDelayMs, stoppingToken);

        // 워밍업 프레임 버리기
        using var warmupFrame = new Mat();
        var warmupSuccess = 0;
        for (var i = 0; i < 5; i++)
        {
            if (_capture.Read(warmupFrame) && !warmupFrame.Empty())
                warmupSuccess++;
            await Task.Delay(100, stoppingToken);
        }

        _logger.LogInformation("워밍업 프레임: {Success}/5 성공", warmupSuccess);

        var delayMs = 1000 / _settings.TargetFps;
        var consecutiveFailures = 0;
        const int maxConsecutiveFailures = 10;
        var connected = false;

        using var frame = new Mat();
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_capture.Read(frame) || frame.Empty())
            {
                consecutiveFailures++;
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    _logger.LogWarning("프레임 읽기 {Count}회 연속 실패. 카메라 연결 끊김.", maxConsecutiveFailures);
                    _isConnected = false;
                    break;
                }

                await Task.Delay(100, stoppingToken);
                continue;
            }

            consecutiveFailures = 0;

            if (!connected)
            {
                _logger.LogInformation("카메라 연결 성공 (프레임 크기: {W}x{H})", frame.Width, frame.Height);
                connected = true;
                _isConnected = true;
            }

            var buf = EncodeToJpeg(frame);
            lock (_frameLock)
            {
                _currentFrame = buf;
            }

            await Task.Delay(delayMs, stoppingToken);
        }
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
