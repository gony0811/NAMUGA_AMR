using AMR.Communication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class CobotMonitoringModel : PageModel
{
    private readonly CobotModbusTcpClient _client;
    private readonly CobotModbusTcpSettings _settings;

    public CobotMonitoringModel(
        CobotModbusTcpClient client,
        CobotModbusTcpSettings settings)
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
            // 기존 연결이 있으면 해제
            if (_client.IsConnected)
                await _client.DisconnectAsync();

            // 공유 설정 업데이트
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

    /// <summary>Cobot 상태 읽기 (Input Register 310~322)</summary>
    public async Task<IActionResult> OnGetStatusAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var status = await _client.ReadCobotStatusAsync();

            return new JsonResult(new
            {
                success = true,
                connected = true,
                data = new
                {
                    enableState = status.EnableState,
                    enableStateLabel = status.EnableState == 1 ? "Enabled" : "Not Enabled",
                    robotMode = status.RobotMode,
                    robotModeLabel = status.RobotMode == 1 ? "Manual" : "Automatic",
                    operationStatus = status.OperationStatus,
                    operationStatusLabel = status.OperationStatus switch
                    {
                        1 => "Stop",
                        2 => "Run",
                        3 => "Pause",
                        4 => "Drag",
                        _ => "-"
                    },
                    toolNo = status.ToolNo,
                    jobNumber = status.JobNumber,
                    scrumState = status.ScrumState,
                    scrumStateLabel = status.ScrumState == 1 ? "비상정지" : "정상",
                    robotStatusFault = status.RobotStatusFault,
                    masterFaultCode = status.MasterFaultCode,
                    subFaultCode = status.SubFaultCode,
                    collisionDetection = status.CollisionDetection,
                    collisionLabel = status.CollisionDetection == 1 ? "충돌" : "정상",
                    motionInPlace = status.MotionInPlace,
                    safetyStopS0 = status.SafetyStopS0,
                    safetyStopS1 = status.SafetyStopS1
                }
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Coil 제어 명령 쓰기</summary>
    public async Task<IActionResult> OnPostWriteCoilAsync([FromBody] CobotCoilWriteRequest request)
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            switch (request.Command)
            {
                case "Pause":
                    await _client.PauseAsync();
                    break;
                case "Recovery":
                    await _client.RecoveryAsync();
                    break;
                case "Start":
                    await _client.StartAsync();
                    break;
                case "Stop":
                    await _client.StopAsync();
                    break;
                case "MoveToJobOrigin":
                    await _client.MoveToJobOriginAsync();
                    break;
                case "ManualAutoSwitch":
                    await _client.ManualAutoSwitchAsync();
                    break;
                case "StartMainProgram":
                    await _client.StartMainProgramAsync();
                    break;
                case "ClearAllFaults":
                    await _client.ClearAllFaultsAsync();
                    break;
                case "WriteDI":
                    await _client.WriteDigitalInputAsync(request.Index, request.Value);
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

    /// <summary>Holding Register (AI) 쓰기</summary>
    public async Task<IActionResult> OnPostWriteRegisterAsync([FromBody] CobotRegisterWriteRequest request)
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            await _client.WriteAnalogInputAsync(request.Index, request.Value);
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Coil Raw 읽기 (제어 명령 영역)</summary>
    public async Task<IActionResult> OnGetRawCoilsAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var coils = await _client.ReadRawCoilsAsync(CobotRegisterMap.Coil.Pause, 11);
            var result = new object[11];
            var names = new Dictionary<int, string>
            {
                { 0, "Pause" }, { 1, "Recovery" }, { 2, "Start" }, { 3, "Stop" },
                { 4, "MoveToJobOrigin" }, { 5, "ManualAutoSwitch" }, { 6, "StartMainProgram" },
                { 10, "ClearAllFaults" }
            };

            for (int i = 0; i < 11; i++)
                result[i] = new
                {
                    address = CobotRegisterMap.Coil.Pause + i,
                    value = coils[i],
                    name = names.TryGetValue(i, out var n) ? n : $"Coil {CobotRegisterMap.Coil.Pause + i}"
                };

            return new JsonResult(new { success = true, coils = result });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Discrete Input Raw 읽기 (DO 영역)</summary>
    public async Task<IActionResult> OnGetRawDiscreteInputsAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var inputs = await _client.ReadRawDiscreteInputsAsync(
                CobotRegisterMap.DiscreteInput.DigitalOutputStart,
                CobotRegisterMap.DiscreteInput.DigitalOutputCount);

            var result = new object[128];
            for (int i = 0; i < 128; i++)
                result[i] = new { index = i, address = 100 + i, value = inputs[i] };

            return new JsonResult(new { success = true, discreteInputs = result });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Input Register Raw 읽기 (AO + 상태)</summary>
    public async Task<IActionResult> OnGetRawInputRegistersAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var aoRegs = await _client.ReadRawInputRegistersAsync(
                CobotRegisterMap.Input.AnalogOutputStart,
                CobotRegisterMap.Input.AnalogOutputCount);
            var aoResult = new object[32];
            for (int i = 0; i < 32; i++)
                aoResult[i] = new { index = i, address = 100 + i, value = aoRegs[i], hex = $"0x{aoRegs[i]:X4}" };

            var statusRegs = await _client.ReadRawInputRegistersAsync(
                CobotRegisterMap.Input.StatusStart,
                CobotRegisterMap.Input.StatusCount);
            var statusResult = new object[13];
            for (int i = 0; i < 13; i++)
                statusResult[i] = new { address = 310 + i, value = statusRegs[i], hex = $"0x{statusRegs[i]:X4}" };

            return new JsonResult(new { success = true, analogOutputs = aoResult, statusRegisters = statusResult });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Input Register + Holding Register 전체 스캔</summary>
    public async Task<IActionResult> OnGetDiagnosticScanAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var nonZeroInputs = new List<object>();
            var nonZeroHoldings = new List<object>();

            for (ushort start = 0; start < 1000; start += 125)
            {
                try
                {
                    var count = (ushort)Math.Min(125, 1000 - start);
                    var regs = await _client.ReadRawInputRegistersAsync(start, count);
                    for (int i = 0; i < regs.Length; i++)
                    {
                        if (regs[i] != 0)
                            nonZeroInputs.Add(new { address = start + i, value = regs[i], hex = $"0x{regs[i]:X4}" });
                    }
                }
                catch { }
            }

            for (ushort start = 0; start < 1000; start += 125)
            {
                try
                {
                    var count = (ushort)Math.Min(125, 1000 - start);
                    var regs = await _client.ReadRawHoldingRegistersAsync(start, count);
                    for (int i = 0; i < regs.Length; i++)
                    {
                        if (regs[i] != 0)
                            nonZeroHoldings.Add(new { address = start + i, value = regs[i], hex = $"0x{regs[i]:X4}" });
                    }
                }
                catch { }
            }

            return new JsonResult(new
            {
                success = true,
                inputRegisters = new { scannedRange = "0-999", nonZeroCount = nonZeroInputs.Count, registers = nonZeroInputs },
                holdingRegisters = new { scannedRange = "0-999", nonZeroCount = nonZeroHoldings.Count, registers = nonZeroHoldings }
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>DI Coil 읽기 (PLC→Robot 비트 입력 100-227)</summary>
    public async Task<IActionResult> OnGetRawDiCoilsAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var coils = await _client.ReadRawCoilsAsync(
                CobotRegisterMap.Coil.DigitalInputStart,
                CobotRegisterMap.Coil.DigitalInputCount);

            var result = new object[128];
            for (int i = 0; i < 128; i++)
                result[i] = new { index = i, address = 100 + i, value = coils[i] };

            return new JsonResult(new { success = true, digitalInputs = result });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Holding Register Raw 읽기 (AI 영역)</summary>
    public async Task<IActionResult> OnGetRawHoldingAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var regs = await _client.ReadRawHoldingRegistersAsync(
                CobotRegisterMap.Holding.AnalogInputStart,
                CobotRegisterMap.Holding.AnalogInputCount);
            var result = new object[32];
            for (int i = 0; i < 32; i++)
                result[i] = new { index = i, address = 100 + i, value = regs[i], hex = $"0x{regs[i]:X4}" };

            return new JsonResult(new { success = true, registers = result });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }
}

public class CobotCoilWriteRequest
{
    public string Command { get; set; } = string.Empty;
    public bool Value { get; set; } = true;
    public ushort Index { get; set; }
}

public class CobotRegisterWriteRequest
{
    public ushort Index { get; set; }
    public ushort Value { get; set; }
}
