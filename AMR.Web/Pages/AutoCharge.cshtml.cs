using AMR.Service;
using AMR.Web.Models;
using AMR.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class AutoChargeModel : PageModel
{
    private readonly IdleChargeService _service;
    private readonly SettingsService _settingsService;

    public AutoChargeModel(IdleChargeService service, SettingsService settingsService)
    {
        _service = service;
        _settingsService = settingsService;
    }

    public void OnGet() { }

    /// <summary>현재 상태 폴링 (AJAX)</summary>
    public IActionResult OnGetState()
    {
        return new JsonResult(new
        {
            enabled = _service.Enabled,
            idleTimeoutSeconds = _service.IdleTimeoutSeconds,
            chargeNodeId = _service.ChargeNodeId,
            idleSeconds = Math.Round(_service.IdleSeconds, 1),
            lastActivityAt = _service.LastActivityAt.ToString("HH:mm:ss"),
            lastTriggerAt = _service.LastTriggerAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            remainingSeconds = Math.Max(0, Math.Round(_service.IdleTimeoutSeconds - _service.IdleSeconds, 1))
        });
    }

    /// <summary>옵션 일괄 적용 (toggle/timeout/node)</summary>
    public IActionResult OnPostSet([FromBody] SetAutoChargeRequest req)
    {
        if (req.Enabled.HasValue) _service.Enabled = req.Enabled.Value;
        if (req.IdleTimeoutSeconds.HasValue)
        {
            var sec = Math.Clamp(req.IdleTimeoutSeconds.Value, 5, 3600);
            _service.IdleTimeoutSeconds = sec;
        }
        if (req.ChargeNodeId is not null) _service.ChargeNodeId = req.ChargeNodeId.Trim();

        // 변경값을 appsettings.json 에 영속화 — 재시작 후에도 그대로 이어진다.
        _settingsService.SaveAutoCharge(new AutoChargeSettings
        {
            Enabled = _service.Enabled,
            IdleTimeoutSeconds = _service.IdleTimeoutSeconds,
            ChargeNodeId = _service.ChargeNodeId
        });

        return new JsonResult(new
        {
            success = true,
            enabled = _service.Enabled,
            idleTimeoutSeconds = _service.IdleTimeoutSeconds,
            chargeNodeId = _service.ChargeNodeId
        });
    }
}

public class SetAutoChargeRequest
{
    public bool? Enabled { get; set; }
    public int? IdleTimeoutSeconds { get; set; }
    public string? ChargeNodeId { get; set; }
}
