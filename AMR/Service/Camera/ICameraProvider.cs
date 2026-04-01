using OpenCvSharp;

namespace AMR.Service.Camera;

public interface ICameraProvider : IDisposable
{
    bool Open(int deviceIndex, int frameWidth, int frameHeight, int depthWidth, int depthHeight);
    bool IsOpened { get; }
    bool GrabFrame();
    bool RetrieveRgb(Mat destination);
    bool RetrieveDepth(Mat destination);
    void Release();
}
