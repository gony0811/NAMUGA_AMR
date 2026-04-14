using AMR.Communication;
using AMR.Data;
using AMR.Models;
using AMR.Service;
using AMR.Web.Models;
using AMR.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AMR.Web.Pages;

public class SettingsModel : PageModel
{
    private readonly SettingsService _settingsService;
    private readonly AmrModbusTcpSettings _modbusSettings;
    private readonly CobotModbusTcpSettings _cobotModbusSettings;
    private readonly AmrService _amrService;
    private readonly AmrDbContext _dbContext;

    public SettingsModel(SettingsService settingsService, AmrModbusTcpSettings modbusSettings,
        CobotModbusTcpSettings cobotModbusSettings, AmrService amrService, AmrDbContext dbContext)
    {
        _settingsService = settingsService;
        _modbusSettings = modbusSettings;
        _cobotModbusSettings = cobotModbusSettings;
        _amrService = amrService;
        _dbContext = dbContext;
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

    // ── 위치 태그 매핑 AJAX 핸들러 ──

    public async Task<IActionResult> OnGetGetMappingsAsync()
    {
        var mappings = await _dbContext.LocationTagMappings
            .OrderBy(m => m.LocationTag)
            .ToListAsync();
        return new JsonResult(mappings);
    }

    public async Task<IActionResult> OnPostSaveMappingAsync([FromBody] LocationTagMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping.LocationTag))
            return new JsonResult(new { success = false, message = "위치 태그를 입력하세요." });

        try
        {
            if (mapping.Id == 0)
            {
                _dbContext.LocationTagMappings.Add(mapping);
            }
            else
            {
                var existing = await _dbContext.LocationTagMappings.FindAsync(mapping.Id);
                if (existing == null)
                    return new JsonResult(new { success = false, message = "매핑을 찾을 수 없습니다." });

                existing.LocationTag = mapping.LocationTag;
                existing.TaskIndex = mapping.TaskIndex;
                existing.JobIndex = mapping.JobIndex;
                existing.Description = mapping.Description;
            }

            await _dbContext.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }
        catch (DbUpdateException)
        {
            return new JsonResult(new { success = false, message = "중복된 위치 태그입니다." });
        }
    }

    public async Task<IActionResult> OnPostDeleteMappingAsync([FromBody] DeleteMappingRequest request)
    {
        var entity = await _dbContext.LocationTagMappings.FindAsync(request.Id);
        if (entity != null)
        {
            _dbContext.LocationTagMappings.Remove(entity);
            await _dbContext.SaveChangesAsync();
        }

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostExecuteMappingAsync([FromBody] ExecuteMappingRequest request)
    {
        var mapping = await _dbContext.LocationTagMappings.FindAsync(request.Id);
        if (mapping == null)
            return new JsonResult(new { success = false, message = "매핑을 찾을 수 없습니다." });

        if (!_amrService.IsConnected)
            return new JsonResult(new { success = false, message = "AMR Modbus TCP가 연결되어 있지 않습니다." });

        try
        {
            await _amrService.SetTaskIndexAsync((ushort)mapping.TaskIndex);
            await _amrService.SetJobIndexAsync((ushort)mapping.JobIndex);
            await _amrService.SetExecutionControlAsync(Enums.ExecutionControl.Start);

            return new JsonResult(new { success = true,
                message = $"실행 완료: {mapping.LocationTag} (Task={mapping.TaskIndex}, Job={mapping.JobIndex})" });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = $"실행 실패: {ex.Message}" });
        }
    }

    public class DeleteMappingRequest
    {
        public int Id { get; set; }
    }

    public class ExecuteMappingRequest
    {
        public int Id { get; set; }
    }
}
