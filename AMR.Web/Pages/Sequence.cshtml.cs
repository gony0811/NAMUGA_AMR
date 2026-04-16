using AMR.Enums;
using AMR.Models;
using AMR.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class SequenceModel : PageModel
{
    private readonly MoveSequenceRunner _runner;

    public SequenceModel(MoveSequenceRunner runner)
    {
        _runner = runner;
    }

    public SequenceState CurrentState => _runner.State;

    public void OnGet()
    {
    }

    /// <summary>시퀀스 상태 폴링 (AJAX)</summary>
    public IActionResult OnGetState()
    {
        var state = _runner.State;
        return new JsonResult(new
        {
            currentStep = (int)state.CurrentStep,
            stepName = state.CurrentStep.ToString(),
            isRunning = state.IsRunning,
            cmdId = state.CmdId ?? "-",
            nodeId = state.NodeId ?? "-",
            port = state.Port ?? "-",
            jobType = state.JobType ?? "-",
            error = state.ErrorMessage,
            startedAt = state.StartedAt?.ToString("HH:mm:ss"),
            stepStartedAt = state.StepStartedAt?.ToString("HH:mm:ss.fff")
        });
    }

    /// <summary>로그 폴링 (AJAX)</summary>
    public IActionResult OnGetLogs(int count = 50)
    {
        var logs = _runner.GetRecentLogs(count);
        return new JsonResult(logs.Select(l => new
        {
            timestamp = l.Timestamp.ToString("HH:mm:ss.fff"),
            step = l.Step.ToString(),
            stepNumber = (int)l.Step,
            message = l.Message,
            isError = l.IsError
        }));
    }

    /// <summary>전체 시퀀스 시작 (AJAX)</summary>
    public async Task<IActionResult> OnPostStartAsync([FromBody] StartSequenceRequest request)
    {
        try
        {
            if (_runner.State.IsRunning)
                return new JsonResult(new { success = false, error = "시퀀스가 이미 실행 중입니다." });

            var command = new AmrCommand
            {
                CmdId = $"web_{DateTime.Now:yyyyMMdd_HHmmss_fff}",
                Command = "moveCmd",
                NodeId = request.NodeId,
                Port = string.IsNullOrEmpty(request.Port) ? null : request.Port,
                JobType = string.IsNullOrEmpty(request.JobType) ? null : request.JobType
            };

            _ = _runner.RunSequenceAsync(command, HttpContext.RequestAborted);
            return new JsonResult(new { success = true, cmdId = command.CmdId });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>단일 Step 수동 실행 (AJAX)</summary>
    public async Task<IActionResult> OnPostExecuteStepAsync([FromBody] ExecuteStepRequest request)
    {
        try
        {
            if (_runner.State.IsRunning)
                return new JsonResult(new { success = false, error = "시퀀스 실행 중에는 수동 Step 실행이 불가합니다." });

            if (!Enum.TryParse<SequenceStep>(request.Step, out var step))
                return new JsonResult(new { success = false, error = $"잘못된 Step: {request.Step}" });

            AmrCommand? command = null;
            if (!string.IsNullOrEmpty(request.NodeId))
            {
                command = new AmrCommand
                {
                    CmdId = $"manual_{DateTime.Now:HHmmss_fff}",
                    Command = "moveCmd",
                    NodeId = request.NodeId,
                    Port = string.IsNullOrEmpty(request.Port) ? null : request.Port,
                    JobType = string.IsNullOrEmpty(request.JobType) ? null : request.JobType
                };
            }

            await _runner.ExecuteStepAsync(step, command, HttpContext.RequestAborted);
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>시퀀스 중단 (AJAX)</summary>
    public IActionResult OnPostAbort()
    {
        try
        {
            _runner.AbortSequence();
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }
}

public class StartSequenceRequest
{
    public string NodeId { get; set; } = "N0001";
    public string? Port { get; set; }
    public string? JobType { get; set; }
}

public class ExecuteStepRequest
{
    public string Step { get; set; } = string.Empty;
    public string? NodeId { get; set; }
    public string? Port { get; set; }
    public string? JobType { get; set; }
}
