using AMR.Communication;
using AMR.Enums;
using AMR.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class StatusRibbonModel : PageModel
{
    private readonly AmrModbusTcpClient _amrClient;
    private readonly CobotModbusTcpClient _cobotClient;
    private readonly IoModuleModbusTcpClient _ioClient;
    private readonly IoModuleService _ioService;

    public StatusRibbonModel(
        AmrModbusTcpClient amrClient,
        CobotModbusTcpClient cobotClient,
        IoModuleModbusTcpClient ioClient,
        IoModuleService ioService)
    {
        _amrClient = amrClient;
        _cobotClient = cobotClient;
        _ioClient = ioClient;
        _ioService = ioService;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnGetStatusAsync()
    {
        var result = new RibbonStatus();

        // AMR
        result.AmrConnected = _amrClient.IsConnected;
        if (_amrClient.IsConnected)
        {
            try
            {
                var s = await _amrClient.ReadRobotStatusAsync();
                result.RobotState = s.RobotState switch
                {
                    RobotState.Stopped => "Stopped",
                    RobotState.Started => "Started",
                    RobotState.Paused => "Paused",
                    _ => "Unknown"
                };
                result.WorkStatus = s.WorkStatus switch
                {
                    WorkStatus.Idle => "Idle",
                    WorkStatus.Moving => "Moving",
                    WorkStatus.Docking => "Docking",
                    WorkStatus.Jog => "Jog",
                    _ => "None"
                };
                result.BatteryLevel = Math.Round(s.Battery.LevelPercent, 1);
            }
            catch { /* 읽기 실패 시 기본값 유지 */ }
        }

        // Cobot
        result.CobotConnected = _cobotClient.IsConnected;
        if (_cobotClient.IsConnected)
        {
            try
            {
                var c = await _cobotClient.ReadCobotStatusAsync();
                result.CobotAutoManual = c.RobotMode == 0 ? "Auto" : "Manual";
                result.CobotProgram = c.OperationStatus switch
                {
                    1 => "Stop",
                    2 => "Run",
                    3 => "Pause",
                    4 => "Drag",
                    _ => "Unknown"
                };
            }
            catch { /* 읽기 실패 시 기본값 유지 */ }
        }

        // IO Module (경광등, 포트 감지)
        result.IoConnected = _ioClient.IsConnected;
        if (_ioClient.IsConnected)
        {
            try
            {
                var outputs = await _ioClient.ReadOutputsAsync();
                result.LampGreen = outputs.TowerLampGreen;
                result.LampYellow = outputs.TowerLampYellow;
                result.LampRed = outputs.TowerLampRed;

                var inputs = await _ioClient.ReadInputsAsync();
                result.Port1 = inputs.MzDetect1;
                result.Port2 = inputs.MzDetect2;
                result.Port3 = inputs.MzDetect3;
                result.Port4 = inputs.MzDetect4;
            }
            catch { /* 읽기 실패 시 기본값 유지 */ }
        }

        return new JsonResult(new { success = true, data = result });
    }

    /// <summary>리셋 (물리 리셋 스��치 짧게 누름과 동일)</summary>
    public async Task<IActionResult> OnPostResetAsync(CancellationToken ct)
    {
        await _ioService.ResetAsync(ct);
        return new JsonResult(new { success = true });
    }

    /// <summary>Manual↔Auto 토글 (물리 리셋 스위치 5초 롱��레스와 동일)</summary>
    public async Task<IActionResult> OnPostManualAutoToggleAsync(CancellationToken ct)
    {
        await _ioService.ManualAutoToggleAsync(ct);
        return new JsonResult(new { success = true });
    }

    /// <summary>부저 OFF</summary>
    public async Task<IActionResult> OnPostBuzzOffAsync(CancellationToken ct)
    {
        await _ioService.BuzzOffAsync(ct);
        return new JsonResult(new { success = true });
    }

    private class RibbonStatus
    {
        public bool AmrConnected { get; set; }
        public string RobotState { get; set; } = "N/A";
        public string WorkStatus { get; set; } = "N/A";
        public double BatteryLevel { get; set; }

        public bool CobotConnected { get; set; }
        public string CobotAutoManual { get; set; } = "N/A";
        public string CobotProgram { get; set; } = "N/A";

        public bool IoConnected { get; set; }
        public bool LampGreen { get; set; }
        public bool LampYellow { get; set; }
        public bool LampRed { get; set; }
        public bool Port1 { get; set; }
        public bool Port2 { get; set; }
        public bool Port3 { get; set; }
        public bool Port4 { get; set; }
    }
}
