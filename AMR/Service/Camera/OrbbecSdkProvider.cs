using System.Runtime.InteropServices;
using OpenCvSharp;

namespace AMR.Service.Camera;

/// <summary>
/// macOS용 Orbbec 카메라 프로바이더 (OrbbecSDK v2 네이티브 라이브러리 사용)
/// </summary>
public class OrbbecSdkProvider : ICameraProvider
{
    private IntPtr _pipeline;
    private IntPtr _config;
    private IntPtr _currentFrameset;
    private bool _isOpened;

    public bool IsOpened => _isOpened;

    public bool Open(int deviceIndex, int frameWidth, int frameHeight, int depthWidth, int depthHeight)
    {
        Release();

        try
        {
            IntPtr error = IntPtr.Zero;

            _pipeline = OrbbecSdkNative.ob_create_pipeline(ref error);
            OrbbecSdkNative.CheckError(error);

            _config = OrbbecSdkNative.ob_create_config(ref error);
            OrbbecSdkNative.CheckError(error);

            // Color 스트림 활성화 (BGR 포맷 요청)
            OrbbecSdkNative.ob_config_enable_video_stream(
                _config, ObStreamType.OB_STREAM_COLOR,
                (uint)frameWidth, (uint)frameHeight, 15, ObFormat.OB_FORMAT_BGR,
                ref error);
            // BGR이 지원되지 않으면 기본 스트림으로 폴백
            if (error != IntPtr.Zero)
            {
                OrbbecSdkNative.ob_delete_error(error);
                error = IntPtr.Zero;
                OrbbecSdkNative.ob_config_enable_stream(_config, ObStreamType.OB_STREAM_COLOR, ref error);
                OrbbecSdkNative.CheckError(error);
            }

            // Depth 스트림 활성화 (Y16 포맷)
            OrbbecSdkNative.ob_config_enable_video_stream(
                _config, ObStreamType.OB_STREAM_DEPTH,
                (uint)depthWidth, (uint)depthHeight, 15, ObFormat.OB_FORMAT_Y16,
                ref error);
            if (error != IntPtr.Zero)
            {
                OrbbecSdkNative.ob_delete_error(error);
                error = IntPtr.Zero;
                OrbbecSdkNative.ob_config_enable_stream(_config, ObStreamType.OB_STREAM_DEPTH, ref error);
                OrbbecSdkNative.CheckError(error);
            }

            OrbbecSdkNative.ob_pipeline_start_with_config(_pipeline, _config, ref error);
            OrbbecSdkNative.CheckError(error);

            _isOpened = true;
            return true;
        }
        catch
        {
            Release();
            return false;
        }
    }

    public bool GrabFrame()
    {
        if (!_isOpened) return false;

        // 이전 프레임셋 해제
        ReleaseCurrentFrameset();

        IntPtr error = IntPtr.Zero;
        _currentFrameset = OrbbecSdkNative.ob_pipeline_wait_for_frameset(_pipeline, 1000, ref error);

        if (error != IntPtr.Zero)
        {
            OrbbecSdkNative.ob_delete_error(error);
            return false;
        }

        return _currentFrameset != IntPtr.Zero;
    }

    public bool RetrieveRgb(Mat destination)
    {
        if (_currentFrameset == IntPtr.Zero) return false;

        IntPtr error = IntPtr.Zero;
        var colorFrame = OrbbecSdkNative.ob_frameset_get_color_frame(_currentFrameset, ref error);
        if (error != IntPtr.Zero || colorFrame == IntPtr.Zero)
        {
            if (error != IntPtr.Zero) OrbbecSdkNative.ob_delete_error(error);
            return false;
        }

        try
        {
            return CopyFrameToMat(colorFrame, destination, isColor: true);
        }
        finally
        {
            error = IntPtr.Zero;
            OrbbecSdkNative.ob_delete_frame(colorFrame, ref error);
            if (error != IntPtr.Zero) OrbbecSdkNative.ob_delete_error(error);
        }
    }

    public bool RetrieveDepth(Mat destination)
    {
        if (_currentFrameset == IntPtr.Zero) return false;

        IntPtr error = IntPtr.Zero;
        var depthFrame = OrbbecSdkNative.ob_frameset_get_depth_frame(_currentFrameset, ref error);
        if (error != IntPtr.Zero || depthFrame == IntPtr.Zero)
        {
            if (error != IntPtr.Zero) OrbbecSdkNative.ob_delete_error(error);
            return false;
        }

        try
        {
            return CopyFrameToMat(depthFrame, destination, isColor: false);
        }
        finally
        {
            error = IntPtr.Zero;
            OrbbecSdkNative.ob_delete_frame(depthFrame, ref error);
            if (error != IntPtr.Zero) OrbbecSdkNative.ob_delete_error(error);
        }
    }

    private static bool CopyFrameToMat(IntPtr frame, Mat destination, bool isColor)
    {
        IntPtr error = IntPtr.Zero;

        var width = (int)OrbbecSdkNative.ob_video_frame_get_width(frame, ref error);
        if (error != IntPtr.Zero) { OrbbecSdkNative.ob_delete_error(error); return false; }

        error = IntPtr.Zero;
        var height = (int)OrbbecSdkNative.ob_video_frame_get_height(frame, ref error);
        if (error != IntPtr.Zero) { OrbbecSdkNative.ob_delete_error(error); return false; }

        error = IntPtr.Zero;
        var format = OrbbecSdkNative.ob_frame_get_format(frame, ref error);
        if (error != IntPtr.Zero) { OrbbecSdkNative.ob_delete_error(error); return false; }

        error = IntPtr.Zero;
        var dataPtr = OrbbecSdkNative.ob_frame_get_data(frame, ref error);
        if (error != IntPtr.Zero || dataPtr == IntPtr.Zero) { if (error != IntPtr.Zero) OrbbecSdkNative.ob_delete_error(error); return false; }

        error = IntPtr.Zero;
        var dataSize = (int)OrbbecSdkNative.ob_frame_get_data_size(frame, ref error);
        if (error != IntPtr.Zero) { OrbbecSdkNative.ob_delete_error(error); return false; }

        if (isColor)
        {
            // Color frame을 BGR Mat으로 변환
            switch (format)
            {
                case ObFormat.OB_FORMAT_BGR:
                {
                    using var temp = Mat.FromPixelData(height, width, MatType.CV_8UC3, dataPtr);
                    temp.CopyTo(destination);
                    break;
                }
                case ObFormat.OB_FORMAT_RGB:
                {
                    using var temp = Mat.FromPixelData(height, width, MatType.CV_8UC3, dataPtr);
                    Cv2.CvtColor(temp, destination, ColorConversionCodes.RGB2BGR);
                    break;
                }
                case ObFormat.OB_FORMAT_BGRA:
                {
                    using var temp = Mat.FromPixelData(height, width, MatType.CV_8UC4, dataPtr);
                    Cv2.CvtColor(temp, destination, ColorConversionCodes.BGRA2BGR);
                    break;
                }
                case ObFormat.OB_FORMAT_RGBA:
                {
                    using var temp = Mat.FromPixelData(height, width, MatType.CV_8UC4, dataPtr);
                    Cv2.CvtColor(temp, destination, ColorConversionCodes.RGBA2BGR);
                    break;
                }
                case ObFormat.OB_FORMAT_YUYV:
                {
                    using var temp = Mat.FromPixelData(height, width, MatType.CV_8UC2, dataPtr);
                    Cv2.CvtColor(temp, destination, ColorConversionCodes.YUV2BGR_YUYV);
                    break;
                }
                case ObFormat.OB_FORMAT_MJPG:
                {
                    var bytes = new byte[dataSize];
                    Marshal.Copy(dataPtr, bytes, 0, dataSize);
                    using var temp = Cv2.ImDecode(bytes, ImreadModes.Color);
                    temp.CopyTo(destination);
                    break;
                }
                default:
                    return false;
            }
        }
        else
        {
            // Depth frame: 16-bit (Y16/Z16)
            using var temp = Mat.FromPixelData(height, width, MatType.CV_16UC1, dataPtr);
            temp.CopyTo(destination);
        }

        return !destination.Empty();
    }

    private void ReleaseCurrentFrameset()
    {
        if (_currentFrameset == IntPtr.Zero) return;
        IntPtr error = IntPtr.Zero;
        OrbbecSdkNative.ob_delete_frame(_currentFrameset, ref error);
        if (error != IntPtr.Zero) OrbbecSdkNative.ob_delete_error(error);
        _currentFrameset = IntPtr.Zero;
    }

    public void Release()
    {
        ReleaseCurrentFrameset();

        IntPtr error;

        if (_pipeline != IntPtr.Zero && _isOpened)
        {
            error = IntPtr.Zero;
            OrbbecSdkNative.ob_pipeline_stop(_pipeline, ref error);
            if (error != IntPtr.Zero) OrbbecSdkNative.ob_delete_error(error);
        }

        if (_config != IntPtr.Zero)
        {
            error = IntPtr.Zero;
            OrbbecSdkNative.ob_delete_config(_config, ref error);
            if (error != IntPtr.Zero) OrbbecSdkNative.ob_delete_error(error);
            _config = IntPtr.Zero;
        }

        if (_pipeline != IntPtr.Zero)
        {
            error = IntPtr.Zero;
            OrbbecSdkNative.ob_delete_pipeline(_pipeline, ref error);
            if (error != IntPtr.Zero) OrbbecSdkNative.ob_delete_error(error);
            _pipeline = IntPtr.Zero;
        }

        _isOpened = false;
    }

    public void Dispose()
    {
        Release();
    }
}
