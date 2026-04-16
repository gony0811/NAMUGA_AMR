using AMR.Communication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class IoModuleMonitoringModel : PageModel
{
    private readonly IoModuleModbusTcpClient _client;
    private readonly IoModuleModbusTcpSettings _settings;

    public IoModuleMonitoringModel(
        IoModuleModbusTcpClient client,
        IoModuleModbusTcpSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public bool IsConnected => _client.IsConnected;
    public string DefaultIp => _settings.IpAddress;
    public int DefaultPort => _settings.Port;
    public byte DefaultSlaveId => _settings.SlaveId;

    public void OnGet() { }

    public async Task<IActionResult> OnPostConnectAsync([FromBody] ConnectRequest request)
    {
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync();

            _settings.IpAddress = request.IpAddress;
            _settings.Port = request.Port;
            _settings.SlaveId = request.SlaveId;

            await _client.ConnectAsync();
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public async Task<IActionResult> OnPostDisconnectAsync()
    {
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync();
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public IActionResult OnGetConnectionStatus()
    {
        return new JsonResult(new { connected = _client.IsConnected });
    }

    /// <summary>입력 상태 읽기 (X000~X005)</summary>
    public async Task<IActionResult> OnGetInputsAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var s = await _client.ReadInputsAsync();
            return new JsonResult(new
            {
                success = true,
                data = new
                {
                    emo = s.Emo,
                    reset = s.Reset,
                    mzDetect1 = s.MzDetect1,
                    mzDetect2 = s.MzDetect2,
                    mzDetect3 = s.MzDetect3,
                    mzDetect4 = s.MzDetect4
                }
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>출력 상태 읽기 (Y000~Y005)</summary>
    public async Task<IActionResult> OnGetOutputsAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var s = await _client.ReadOutputsAsync();
            return new JsonResult(new
            {
                success = true,
                data = new
                {
                    towerLampRed = s.TowerLampRed,
                    towerLampYellow = s.TowerLampYellow,
                    towerLampGreen = s.TowerLampGreen,
                    towerLampBuzzer = s.TowerLampBuzzer,
                    resetSwLamp = s.ResetSwLamp,
                    cobotServoOnOff = s.CobotServoOnOff
                }
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>출력 제어 (래칭 ON/OFF)</summary>
    public async Task<IActionResult> OnPostWriteOutputAsync([FromBody] IoModuleOutputWriteRequest request)
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            switch (request.Command)
            {
                case "TowerLampRed":
                    await _client.SetTowerLampRedAsync(request.Value);
                    break;
                case "TowerLampYellow":
                    await _client.SetTowerLampYellowAsync(request.Value);
                    break;
                case "TowerLampGreen":
                    await _client.SetTowerLampGreenAsync(request.Value);
                    break;
                case "TowerLampBuzzer":
                    await _client.SetTowerLampBuzzerAsync(request.Value);
                    break;
                case "ResetSwLamp":
                    await _client.SetResetSwLampAsync(request.Value);
                    break;
                case "CobotServoOnOff":
                    await _client.SetCobotServoAsync(request.Value);
                    break;
                case "AllTowerLampsOff":
                    await _client.AllTowerLampsOffAsync();
                    break;
                default:
                    return new JsonResult(new { success = false, error = $"알 수 없는 명령: {request.Command}" });
            }

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Discrete Input 원시 읽기 (0~15)</summary>
    public async Task<IActionResult> OnGetRawDiscreteInputsAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var bits = await _client.ReadRawDiscreteInputsAsync(0, 16);
            var result = new object[bits.Length];
            for (int i = 0; i < bits.Length; i++)
                result[i] = new { index = i, address = i, value = bits[i] };

            return new JsonResult(new { success = true, discreteInputs = result });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Coil 원시 읽기 (0~15)</summary>
    public async Task<IActionResult> OnGetRawCoilsAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var bits = await _client.ReadRawCoilsAsync(0, 16);
            var result = new object[bits.Length];
            for (int i = 0; i < bits.Length; i++)
                result[i] = new { index = i, address = i, value = bits[i] };

            return new JsonResult(new { success = true, coils = result });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

}

public class IoModuleOutputWriteRequest
{
    public string Command { get; set; } = string.Empty;
    public bool Value { get; set; }
}
