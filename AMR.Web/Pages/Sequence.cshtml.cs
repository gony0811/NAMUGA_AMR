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
    private readonly SequenceSimulator _simulator;

    public SequenceModel(MoveSequenceRunner runner, MqttService mqttService, SequenceSimulator simulator)
    {
        _runner = runner;
        _mqttService = mqttService;
        _simulator = simulator;
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
        public string? Type { get; set; }    // EXCHANGE 게이트: UNLOAD(취출 허가) / LOAD(투입 허가)
        public string? JobId { get; set; }   // EXCHANGE Job ID (비우면 게이트에서 무조건 수용)
    }

    /// <summary>
    /// 설비포트 시퀀스의 WaitActionCmd 단계 / EXCHANGE 게이트 트리거 — ActionCmd 를 MQTT 큐에 수동 주입.
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
                AmrSlot = req.AmrSlot,
                Type = string.IsNullOrWhiteSpace(req.Type) ? null : req.Type,
                JobId = string.IsNullOrWhiteSpace(req.JobId) ? null : req.JobId
            };
            // v0.3: 설비 앞 도킹 대기(ExchangeDocked) 상태면 독립 작업으로 실행, 그 외엔 큐 주입(설비포트 moveCmd Step5)
            var st = _runner.State;
            if (st.IsExchangeDocked && !st.IsRunning && !string.IsNullOrWhiteSpace(cmd.Type))
            {
                cmd.JobType ??= "EXCHANGE";
                cmd.JobId ??= st.JobId;
                cmd.NodeId = st.NodeId ?? cmd.NodeId;
                cmd.Port ??= st.Port;
                _ = _runner.RunActionAsync(cmd, HttpContext.RequestAborted);
                return new JsonResult(new { success = true, mode = "action", cmdId = cmd.CmdId, type = cmd.Type, amrSlot = cmd.AmrSlot, jobId = cmd.JobId });
            }

            _mqttService.InjectActionCmdForTest(cmd);
            return new JsonResult(new { success = true, mode = "queue", cmdId = cmd.CmdId, port = cmd.Port, amrSlot = cmd.AmrSlot, type = cmd.Type, jobId = cmd.JobId });
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
            demoStepIndex = state.DemoStepIndex,
            isExchangeDocked = state.IsExchangeDocked,
            jobId = state.JobId ?? "-",
            lastActionType = state.LastActionType ?? "-"
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

            var cmdId = string.IsNullOrWhiteSpace(request.JobId) ? $"web_{DateTime.Now:yyyyMMdd_HHmmss_fff}" : request.JobId!;
            var command = new AmrCommand
            {
                CmdId = cmdId,
                JobId = cmdId,
                Command = "moveCmd",
                NodeId = request.NodeId,
                Port = string.IsNullOrEmpty(request.Port) ? null : request.Port,
                JobType = string.IsNullOrEmpty(request.JobType) ? null : request.JobType,
                PortType = string.IsNullOrEmpty(request.PortType) ? null : request.PortType,
                AmrSlot = request.AmrSlot,
                Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model
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

    #region 시뮬레이션 모드 (사무실 테스트 — 실장비 없이 수동 확인으로 진행)

    /// <summary>시뮬레이션 상태 폴링 (AJAX)</summary>
    public IActionResult OnGetSimState()
    {
        return new JsonResult(new
        {
            enabled = _simulator.Enabled,
            pendingAction = _simulator.PendingAction,
            amrSlots = _simulator.AmrSlots,
            materialSlot1 = _simulator.MaterialSlot1,
            materialSlot2 = _simulator.MaterialSlot2,
            poseX = _simulator.Pose.X,
            poseY = _simulator.Pose.Y,
            poseAngle = _simulator.Pose.Angle,
            moving = _simulator.Moving
        });
    }

    /// <summary>가상 AMR 좌표 직접 설정 (초기 위치 등)</summary>
    public IActionResult OnPostSimSetPose([FromBody] SimSetPoseRequest req)
    {
        _simulator.SetPose(req.X, req.Y, req.Angle);
        return new JsonResult(new { success = true });
    }

    public class SimSetPoseRequest { public double X { get; set; } public double Y { get; set; } public double Angle { get; set; } }

    /// <summary>시뮬레이션 모드 ON/OFF</summary>
    public IActionResult OnPostSimToggle([FromBody] SimToggleRequest req)
    {
        try
        {
            if (req.Enabled && _runner.State.IsRunning)
                return new JsonResult(new { success = false, error = "시퀀스 실행 중에는 모드를 켤 수 없습니다. 완료 또는 Abort 후 켜세요." });

            _simulator.SetEnabled(req.Enabled);
            return new JsonResult(new { success = true, enabled = _simulator.Enabled });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>대기 중인 동작 완료 확인 ('동작 완료' 버튼)</summary>
    public IActionResult OnPostSimConfirm()
    {
        var ok = _simulator.ConfirmPending();
        return new JsonResult(new { success = ok, error = ok ? null : "대기 중인 동작이 없습니다." });
    }

    /// <summary>대기 중인 동작 실패 주입 (오류 경로 테스트)</summary>
    public IActionResult OnPostSimFail()
    {
        var ok = _simulator.FailPending();
        return new JsonResult(new { success = ok, error = ok ? null : "대기 중인 동작이 없습니다." });
    }

    /// <summary>가상 슬롯 상태 설정 — target: amr1~4 / mat1 / mat2</summary>
    public IActionResult OnPostSimSetSlot([FromBody] SimSetSlotRequest req)
    {
        try
        {
            switch (req.Target)
            {
                case "amr1" or "amr2" or "amr3" or "amr4":
                    _simulator.SetAmrSlot(int.Parse(req.Target[3..]), req.Occupied);
                    break;
                case "mat1":
                    _simulator.MaterialSlot1 = req.Occupied;
                    break;
                case "mat2":
                    _simulator.MaterialSlot2 = req.Occupied;
                    break;
                default:
                    return new JsonResult(new { success = false, error = $"잘못된 대상: {req.Target}" });
            }
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public class SimToggleRequest { public bool Enabled { get; set; } }
    public class SimSetSlotRequest { public string Target { get; set; } = ""; public bool Occupied { get; set; } }

    #endregion
}

public class StartSequenceRequest
{
    public string NodeId { get; set; } = "N0001";
    public string? Port { get; set; }
    public string? JobType { get; set; }
    public string? PortType { get; set; }
    public int AmrSlot { get; set; } = 1;
    public string? Model { get; set; }
    public string? JobId { get; set; }   // EXCHANGE 테스트: cmdId=jobId 로 사용
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
