using AMR.Communication;
using AMR.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class AmrMonitoringModel : PageModel
{
    private static AmrModbusTcpClient? _client;
    private static readonly object _lock = new();

    private readonly AmrModbusTcpSettings _defaultSettings;

    public AmrMonitoringModel(AmrModbusTcpSettings defaultSettings)
    {
        _defaultSettings = defaultSettings;
    }

    public bool IsConnected => _client?.IsConnected ?? false;
    public string DefaultIp => _defaultSettings.IpAddress;
    public int DefaultPort => _defaultSettings.Port;
    public byte DefaultSlaveId => _defaultSettings.SlaveId;

    public void OnGet() { }

    public async Task<IActionResult> OnPostConnectAsync([FromBody] ConnectRequest request)
    {
        try
        {
            lock (_lock)
            {
                if (_client is { IsConnected: true })
                {
                    _client.Disconnect();
                    _client.Dispose();
                }

                var settings = new AmrModbusTcpSettings
                {
                    IpAddress = request.IpAddress,
                    Port = request.Port,
                    SlaveId = request.SlaveId
                };
                _client = new AmrModbusTcpClient(settings);
            }

            await _client!.ConnectAsync();
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public IActionResult OnPostDisconnect()
    {
        try
        {
            lock (_lock)
            {
                if (_client != null)
                {
                    _client.Disconnect();
                    _client.Dispose();
                    _client = null;
                }
            }
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public async Task<IActionResult> OnGetStatusAsync()
    {
        try
        {
            if (_client is not { IsConnected: true })
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var status = await _client.ReadRobotStatusAsync();
            return new JsonResult(new
            {
                success = true,
                connected = true,
                data = new
                {
                    powerState = EnumLabel(status.PowerState),
                    robotState = EnumLabel(status.RobotState),
                    errorCode = status.ErrorCode,
                    robotStopActive = status.RobotStopActive,
                    wifi = EnumLabel(status.WiFi),
                    workStatus = EnumLabel(status.WorkStatus),
                    drivingMode = EnumLabel(status.DrivingMode),
                    poseX = Math.Round(status.Pose.X, 3),
                    poseY = Math.Round(status.Pose.Y, 3),
                    poseAngle = Math.Round(status.Pose.Angle, 3),
                    mapStatusPercent = Math.Round(status.MapStatusPercent, 2),
                    batteryLevel = Math.Round(status.Battery.LevelPercent, 2),
                    batteryVoltage = Math.Round(status.Battery.Voltage, 2),
                    batteryCurrent = Math.Round(status.Battery.Current, 2),
                    batteryTemp = Math.Round(status.Battery.TemperatureCelsius, 2),
                    chargingState = EnumLabel(status.Battery.ChargingState),
                    totalTaskCount = status.TaskProgress.TotalTaskCount,
                    currentTaskNumber = status.TaskProgress.CurrentTaskNumber,
                    totalJobCount = status.TaskProgress.TotalJobCount,
                    currentJobNumber = status.TaskProgress.CurrentJobNumber
                }
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public IActionResult OnGetConnectionStatus()
    {
        return new JsonResult(new { connected = _client?.IsConnected ?? false });
    }

    /// <summary>Pose 레지스터 영역 Raw 값 + 바이트 순서 해석 (진단용)</summary>
    public async Task<IActionResult> OnGetRawPoseAsync()
    {
        try
        {
            if (_client is not { IsConnected: true })
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var regs = await _client.ReadRawInputRegistersAsync(19, 6);

            object InterpretFloat(ushort r0, ushort r1) => new
            {
                raw_r0 = r0,
                raw_r1 = r1,
                AB_CD = BitConverter.Int32BitsToSingle((int)(((uint)r0 << 16) | r1)),
                CD_AB = BitConverter.Int32BitsToSingle((int)(((uint)r1 << 16) | r0)),
            };

            return new JsonResult(new
            {
                success = true,
                poseX = InterpretFloat(regs[0], regs[1]),
                poseY = InterpretFloat(regs[2], regs[3]),
                poseAngle = InterpretFloat(regs[4], regs[5])
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>기본 상태 레지스터 영역 Raw 값 (진단용)</summary>
    public async Task<IActionResult> OnGetRawStatusAsync()
    {
        try
        {
            if (_client is not { IsConnected: true })
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var regs = await _client.ReadRawInputRegistersAsync(0, 16);
            var result = new object[16];
            for (int i = 0; i < 16; i++)
                result[i] = new { address = i, value = regs[i], hex = $"0x{regs[i]:X4}" };

            return new JsonResult(new { success = true, registers = result });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Holding Register 영역 Raw 값 (진단용)</summary>
    public async Task<IActionResult> OnGetRawHoldingAsync()
    {
        try
        {
            if (_client is not { IsConnected: true })
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            var regs = await _client.ReadRawHoldingRegistersAsync(0, 33);
            var result = new object[33];
            for (int i = 0; i < 33; i++)
                result[i] = new { address = i, value = regs[i], hex = $"0x{regs[i]:X4}" };

            return new JsonResult(new { success = true, registers = result });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public async Task<IActionResult> OnPostWriteAsync([FromBody] WriteRequest request)
    {
        try
        {
            if (_client is not { IsConnected: true })
                return new JsonResult(new { success = false, error = "연결되지 않음" });

            switch (request.Command)
            {
                case "Power":
                    await _client.SetPowerAsync((PowerCommand)request.Value);
                    break;
                case "DrivingMode":
                    await _client.SetDrivingModeAsync((DrivingMode)request.Value);
                    break;
                case "ExecutionControl":
                    await _client.SetExecutionControlAsync((ExecutionControl)request.Value);
                    break;
                case "RobotStop":
                    await _client.SetRobotStopAsync((ushort)request.Value);
                    break;
                case "AirInitialize":
                    await _client.AirInitializeAsync();
                    break;
                case "TaskIndex":
                    await _client.SetTaskIndexAsync((ushort)request.Value);
                    break;
                case "JobIndex":
                    await _client.SetJobIndexAsync((ushort)request.Value);
                    break;
                case "TaskJob":
                    await _client.SetTaskIndexAsync((ushort)request.TaskIndex);
                    await _client.SetJobIndexAsync((ushort)request.JobIndex);
                    break;
                case "PoseSearch":
                    await _client.SetPoseSearchAsync((ushort)request.Value);
                    break;
                case "PoseTarget":
                    await _client.SetPoseTargetAsync(request.PoseX, request.PoseY, request.PoseAngle);
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

    /// <summary>Enum 값이 정의되지 않은 경우(0 등) "-" 반환, 정의된 경우 이름 반환</summary>
    private static string EnumLabel<T>(T value) where T : struct, Enum
        => Enum.IsDefined(value) ? value.ToString() : "-";
}

public class ConnectRequest
{
    public string IpAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public byte SlaveId { get; set; } = 1;
}

public class WriteRequest
{
    public string Command { get; set; } = string.Empty;
    public int Value { get; set; }
    public float PoseX { get; set; }
    public float PoseY { get; set; }
    public float PoseAngle { get; set; }
    public int TaskIndex { get; set; }
    public int JobIndex { get; set; }
}
