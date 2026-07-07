using System.Text;
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
    private readonly MqttService _mqttService;

    public SequenceModel(MoveSequenceRunner runner, MqttService mqttService)
    {
        _runner = runner;
        _mqttService = mqttService;
    }

    public SequenceState CurrentState => _runner.State;

    public void OnGet()
    {
    }

    /// <summary>테스트용 — Web UI 에서 ActionCmd 수동 주입 요청 본문</summary>
    public class SendActionCmdRequest
    {
        public string? Port { get; set; }
        public int AmrSlot { get; set; } = 1;
        public string? CmdId { get; set; }   // 비어있으면 자동 생성
    }

    /// <summary>
    /// 설비포트 시퀀스의 WaitActionCmd 단계 트리거 — ActionCmd 를 MQTT 큐에 수동 주입.
    /// ACS 없이 테스트할 때 사용.
    /// </summary>
    public IActionResult OnPostSendActionCmd([FromBody] SendActionCmdRequest req)
    {
        try
        {
            var cmd = new AmrCommand
            {
                CmdId = string.IsNullOrWhiteSpace(req.CmdId)
                    ? $"web_action_{DateTime.Now:yyyyMMdd_HHmmss_fff}"
                    : req.CmdId,
                Command = "actionCmd",
                Port = string.IsNullOrWhiteSpace(req.Port) ? null : req.Port,
                AmrSlot = req.AmrSlot
            };
            _mqttService.InjectActionCmdForTest(cmd);
            return new JsonResult(new { success = true, cmdId = cmd.CmdId, port = cmd.Port, amrSlot = cmd.AmrSlot });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
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
            portType = state.PortType ?? "-",
            amrSlot = state.AmrSlot,
            error = state.ErrorMessage,
            startedAt = state.StartedAt?.ToString("HH:mm:ss"),
            stepStartedAt = state.StepStartedAt?.ToString("HH:mm:ss.fff"),
            isDemoRunning = state.IsDemoRunning,
            demoCycle = state.DemoCycle,
            demoStepIndex = state.DemoStepIndex
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

    /// <summary>로그 CSV 다운로드</summary>
    public IActionResult OnGetExportCsv(int count = 200)
    {
        var logs = _runner.GetRecentLogs(count);

        var sb = new StringBuilder();
        sb.Append('\uFEFF'); // UTF-8 BOM (Excel에서 한글 깨짐 방지)
        sb.AppendLine("Timestamp,StepNumber,Step,IsError,Message");

        foreach (var l in logs)
        {
            sb.Append(l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(',');
            sb.Append((int)l.Step).Append(',');
            sb.Append(l.Step).Append(',');
            sb.Append(l.IsError ? "1" : "0").Append(',');
            sb.AppendLine(CsvEscape(l.Message));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"sequence_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv", fileName);
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuote ? $"\"{escaped}\"" : escaped;
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
                JobType = string.IsNullOrEmpty(request.JobType) ? null : request.JobType,
                PortType = string.IsNullOrEmpty(request.PortType) ? null : request.PortType,
                AmrSlot = request.AmrSlot
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
                    JobType = string.IsNullOrEmpty(request.JobType) ? null : request.JobType,
                    PortType = string.IsNullOrEmpty(request.PortType) ? null : request.PortType,
                    AmrSlot = request.AmrSlot
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

    /// <summary>데모 모드 시작 (AJAX)</summary>
    public IActionResult OnPostStartDemo()
    {
        try
        {
            if (_runner.State.IsRunning || _runner.State.IsDemoRunning)
                return new JsonResult(new { success = false, error = "시퀀스 또는 데모가 이미 실행 중입니다." });

            _ = _runner.RunDemoAsync(HttpContext.RequestAborted);
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>데모 모드 정지 (AJAX)</summary>
    public IActionResult OnPostStopDemo()
    {
        try
        {
            _runner.StopDemo();
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
    public string? PortType { get; set; }
    public int AmrSlot { get; set; } = 1;
}

public class ExecuteStepRequest
{
    public string Step { get; set; } = string.Empty;
    public string? NodeId { get; set; }
    public string? Port { get; set; }
    public string? JobType { get; set; }
    public string? PortType { get; set; }
    public int AmrSlot { get; set; } = 1;
}
