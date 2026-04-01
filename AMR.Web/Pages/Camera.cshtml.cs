using System.Text;
using AMR.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class CameraModel : PageModel
{
    private readonly CameraService _cameraService;

    public CameraModel(CameraService cameraService)
    {
        _cameraService = cameraService;
    }

    public bool IsConnected => _cameraService.IsConnected;

    public void OnGet() { }

    public IActionResult OnGetStatus()
    {
        return new JsonResult(new { connected = _cameraService.IsConnected });
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
            DetectedAt = result.Detected ? result.DetectedAt.ToString("HH:mm:ss.fff") : ""
        });
    }

    public async Task OnGetRgbStream()
    {
        await StreamFrames(() => _cameraService.GetCurrentRgbFrame());
    }

    public async Task OnGetDepthStream()
    {
        await StreamFrames(() => _cameraService.GetCurrentDepthFrame());
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
