using OpenCvSharp;

namespace AMR.Service.Camera;

/// <summary>
/// Windows/Linux용 Orbbec 카메라 프로바이더 (OpenCV OBSENSOR 백엔드)
/// </summary>
public class OpenCvObsensorProvider : ICameraProvider
{
    private const int CAP_OBSENSOR = 2600;
    private VideoCapture? _capture;

    public bool IsOpened => _capture?.IsOpened() ?? false;

    public bool Open(int deviceIndex, int frameWidth, int frameHeight, int depthWidth, int depthHeight)
    {
        Release();
        _capture = new VideoCapture(deviceIndex, (VideoCaptureAPIs)CAP_OBSENSOR);

        if (!_capture.IsOpened())
            return false;

        _capture.Set(VideoCaptureProperties.FrameWidth, frameWidth);
        _capture.Set(VideoCaptureProperties.FrameHeight, frameHeight);
        return true;
    }

    public bool GrabFrame()
    {
        return _capture?.Grab() ?? false;
    }

    public bool RetrieveRgb(Mat destination)
    {
        if (_capture is null) return false;
        return _capture.Retrieve(destination, 1) && !destination.Empty();
    }

    public bool RetrieveDepth(Mat destination)
    {
        if (_capture is null) return false;
        return _capture.Retrieve(destination, 0) && !destination.Empty();
    }

    public void Release()
    {
        if (_capture is not null)
        {
            _capture.Release();
            _capture.Dispose();
            _capture = null;
        }
    }

    public void Dispose()
    {
        Release();
    }
}
