using AMR.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class SoundModel : PageModel
{
    private readonly MovementSoundService _soundService;

    public SoundModel(MovementSoundService soundService)
    {
        _soundService = soundService;
    }

    public bool Enabled => _soundService.Enabled;

    public void OnGet() { }

    /// <summary>현재 상태 폴링 (AJAX)</summary>
    public IActionResult OnGetState()
    {
        return new JsonResult(new
        {
            enabled = _soundService.Enabled,
            robotState = _soundService.LastRobotState?.ToString() ?? "Unknown",
            isPlaying = _soundService.Enabled && _soundService.LastRobotState?.ToString() == "Started",
            intervalMs = _soundService.IntervalMs,
            noteDurationMs = _soundService.NoteDurationMs,
            frequencies = _soundService.MelodyFrequencies,
            usingWavFile = _soundService.UsingWavFile
        });
    }

    /// <summary>활성/비활성 토글</summary>
    public IActionResult OnPostToggle()
    {
        _soundService.Enabled = !_soundService.Enabled;
        return new JsonResult(new { success = true, enabled = _soundService.Enabled });
    }

    /// <summary>명시적 ON/OFF</summary>
    public IActionResult OnPostSet([FromBody] SetSoundRequest request)
    {
        _soundService.Enabled = request.Enabled;
        return new JsonResult(new { success = true, enabled = _soundService.Enabled });
    }
}

public class SetSoundRequest
{
    public bool Enabled { get; set; }
}
