using AMR.Enums;
using AMR.Models;
using AMR.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class SimulationModel : PageModel
{
    private readonly SimulationService _simulationService;

    public SimulationModel(SimulationService simulationService)
    {
        _simulationService = simulationService;
    }

    public bool IsConnected => _simulationService.IsConnected;

    public void OnGet()
    {
    }

    /// <summary>AMR 상태 퍼블리시 (AJAX)</summary>
    public async Task<IActionResult> OnPostPublishStatusAsync([FromBody] StatusRequest request)
    {
        try
        {
            var statusMessage = new AmrStatusMessage
            {
                State = new AmrStateInfo
                {
                    RunState = Enum.Parse<RunState>(request.RunState),
                    FullState = Enum.Parse<FullState>(request.FullState),
                    WorkState = Enum.Parse<WorkStatus>(request.WorkState),
                    VehicleDestNode = request.VehicleDestNode
                },
                Pose = new RobotPose(request.PoseX, request.PoseY, request.PoseAngle),
                Error = new ErrorInfo
                {
                    Code = request.ErrorCode,
                    Name = request.ErrorMessage
                },
                Battery = new BatteryStatus
                {
                    LevelPercent = request.BatteryPercent,
                    Voltage = request.BatteryVoltage,
                    Current = request.BatteryCurrent,
                    TemperatureCelsius = request.BatteryTemp,
                    ChargingState = Enum.Parse<ChargingState>(request.ChargingState)
                },
                Abnormal = string.IsNullOrEmpty(request.AbnormalType)
                    ? null
                    : new AbnormalInfo
                    {
                        Type = request.AbnormalType,
                        Node = request.AbnormalNode ?? string.Empty,
                        Timestamp = DateTime.UtcNow.ToString("o")
                    }
            };

            await _simulationService.PublishStatusAsync(statusMessage);
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Reply 송신 (AJAX)</summary>
    public async Task<IActionResult> OnPostSendReplyAsync([FromBody] ReplyRequest request)
    {
        try
        {
            var reply = new CommandReply
            {
                CmdId = request.CmdId,
                Status = request.Status,
                ResultCode = request.ResultCode,
                Message = request.Message,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            await _simulationService.PublishReplyAsync(reply);
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>수신 명령 폴링 (AJAX)</summary>
    public IActionResult OnGetCommands(int sinceIndex)
    {
        var commands = _simulationService.GetCommands(sinceIndex);
        var totalCount = _simulationService.CommandCount;

        return new JsonResult(new
        {
            commands = commands.Select(c => new
            {
                receivedAt = c.ReceivedAt.ToString("HH:mm:ss.fff"),
                cmdId = c.Command.CmdId,
                command = c.Command.Command,
                nodeId = c.Command.NodeId,
                port = c.Command.Port ?? "-",
                jobType = c.Command.JobType ?? "-"
            }),
            totalCount
        });
    }
}

public class StatusRequest
{
    public string RunState { get; set; } = "Run";
    public string FullState { get; set; } = "Empty";
    public string WorkState { get; set; } = "Idle";
    public string VehicleDestNode { get; set; } = string.Empty;
    public float PoseX { get; set; }
    public float PoseY { get; set; }
    public float PoseAngle { get; set; }
    public ushort ErrorCode { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public float BatteryPercent { get; set; } = 100;
    public float BatteryVoltage { get; set; }
    public float BatteryCurrent { get; set; }
    public float BatteryTemp { get; set; }
    public string ChargingState { get; set; } = "Discharging";
    public string? AbnormalType { get; set; }
    public string? AbnormalNode { get; set; }
}

public class ReplyRequest
{
    public string CmdId { get; set; } = string.Empty;
    public string Status { get; set; } = "ACCEPTED";
    public int ResultCode { get; set; }
    public string Message { get; set; } = string.Empty;
}
