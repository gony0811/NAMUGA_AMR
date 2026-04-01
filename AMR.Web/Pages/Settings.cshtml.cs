using AMR.Communication;
using AMR.Service;
using AMR.Web.Models;
using AMR.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

public class SettingsModel : PageModel
{
    private readonly SettingsService _settingsService;
    private readonly AmrModbusTcpSettings _modbusSettings;
    private readonly CobotModbusTcpSettings _cobotModbusSettings;
    private readonly AmrService _amrService;

    public SettingsModel(SettingsService settingsService, AmrModbusTcpSettings modbusSettings,
        CobotModbusTcpSettings cobotModbusSettings, AmrService amrService)
    {
        _settingsService = settingsService;
        _modbusSettings = modbusSettings;
        _cobotModbusSettings = cobotModbusSettings;
        _amrService = amrService;
    }

    [BindProperty]
    public MqttSettings MqttSettings { get; set; } = new();

    [BindProperty]
    public ModbusSettings ModbusSettings { get; set; } = new();

    [BindProperty]
    public CobotModbusSettings CobotModbusSettings { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public void OnGet()
    {
        MqttSettings = _settingsService.LoadMqtt();
        ModbusSettings = _settingsService.LoadModbus();
        CobotModbusSettings = _settingsService.LoadCobotModbus();
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            _settingsService.Save(MqttSettings, ModbusSettings, CobotModbusSettings);

            // AMR 인메모리 싱글톤 설정 업데이트 후 재연결 트리거
            _modbusSettings.IpAddress = ModbusSettings.IpAddress;
            _modbusSettings.Port = ModbusSettings.Port;
            _modbusSettings.SlaveId = ModbusSettings.SlaveId;
            _amrService.Disconnect();

            // Cobot 인메모리 싱글톤 설정 업데이트
            _cobotModbusSettings.IpAddress = CobotModbusSettings.IpAddress;
            _cobotModbusSettings.Port = CobotModbusSettings.Port;
            _cobotModbusSettings.SlaveId = CobotModbusSettings.SlaveId;

            StatusMessage = "설정이 저장되었습니다. Modbus TCP 재연결을 시도합니다.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"저장 실패: {ex.Message}";
        }

        return RedirectToPage();
    }
}
