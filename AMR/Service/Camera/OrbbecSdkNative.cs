using System.Runtime.InteropServices;

namespace AMR.Service.Camera;

/// <summary>
/// OrbbecSDK v2 C API P/Invoke 바인딩 (macOS용)
/// </summary>
internal static class OrbbecSdkNative
{
    private const string LibName = "OrbbecSDK";

    // ── Error ──
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ob_error_get_message(IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ob_delete_error(IntPtr error);

    // ── Context ──
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ob_create_context(ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ob_delete_context(IntPtr context, ref IntPtr error);

    // ── Pipeline ──
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ob_create_pipeline(ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ob_delete_pipeline(IntPtr pipeline, ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ob_pipeline_start_with_config(IntPtr pipeline, IntPtr config, ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ob_pipeline_stop(IntPtr pipeline, ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ob_pipeline_wait_for_frameset(IntPtr pipeline, uint timeoutMs, ref IntPtr error);

    // ── Config ──
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ob_create_config(ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ob_delete_config(IntPtr config, ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ob_config_enable_video_stream(
        IntPtr config, ObStreamType streamType,
        uint width, uint height, uint fps, ObFormat format,
        ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ob_config_enable_stream(IntPtr config, ObStreamType streamType, ref IntPtr error);

    // ── Frameset ──
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ob_frameset_get_color_frame(IntPtr frameset, ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ob_frameset_get_depth_frame(IntPtr frameset, ref IntPtr error);

    // ── Frame ──
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ob_frame_get_data(IntPtr frame, ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint ob_frame_get_data_size(IntPtr frame, ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint ob_video_frame_get_width(IntPtr frame, ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint ob_video_frame_get_height(IntPtr frame, ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ObFormat ob_frame_get_format(IntPtr frame, ref IntPtr error);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ob_delete_frame(IntPtr frame, ref IntPtr error);

    // ── Helper ──
    internal static void CheckError(IntPtr error)
    {
        if (error == IntPtr.Zero) return;

        var msgPtr = ob_error_get_message(error);
        var msg = Marshal.PtrToStringAnsi(msgPtr) ?? "Unknown OrbbecSDK error";
        ob_delete_error(error);
        throw new OrbbecException(msg);
    }
}

internal enum ObStreamType
{
    OB_STREAM_COLOR = 2,
    OB_STREAM_DEPTH = 3,
    OB_STREAM_IR = 4,
}

internal enum ObFormat
{
    OB_FORMAT_RGB = 1,
    OB_FORMAT_BGR = 2,
    OB_FORMAT_Y16 = 8,
    OB_FORMAT_Z16 = 30,
    OB_FORMAT_MJPG = 6,
    OB_FORMAT_YUYV = 3,
    OB_FORMAT_NV12 = 5,
    OB_FORMAT_BGRA = 22,
    OB_FORMAT_RGBA = 23,
}

internal class OrbbecException : Exception
{
    public OrbbecException(string message) : base(message) { }
}
