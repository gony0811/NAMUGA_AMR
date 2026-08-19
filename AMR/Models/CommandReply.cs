using System.Text.Json.Serialization;

namespace AMR.Models;

/// <summary>
/// AMR → ACS 명령 응답 (amr/{ClientId}/reply)
/// </summary>
public class CommandReply
{
    /// <summary>원본 명령 ID</summary>
    public string CmdId { get; set; } = string.Empty;

    /// <summary>상태 (ACCEPTED, REJECTED, EXECUTING, ARRIVED, STEP_COMPLETE, COMPLETED, FAILED, CANCELED)</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>결과 코드 (0: 성공, 기타: 에러 코드 — docs/ACS-AMR_mqtt_exchangecmd.md 8.1)</summary>
    public int ResultCode { get; set; }

    /// <summary>상세 메시지</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>작업 유형 (LOAD, UNLOAD, EXCHANGE)</summary>
    public string? JobType { get; set; }

    /// <summary>타임스탬프 (ISO 8601)</summary>
    public string Timestamp { get; set; } = string.Empty;

    // ===== EXCHANGE 확장 필드 — 교환 응답에만 실림 (moveCmd 응답은 기존과 동일: null 생략) =====

    /// <summary>ACS Exchange Job ID (exchangeCmd의 jobId 그대로)</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JobId { get; set; }

    /// <summary>단계 코드 (10/20/30/40/50/60) — MES 사양과 동일 값</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Step { get; set; }

    /// <summary>단계 이름 (PICKUP_NEW / MOVE_TO_EQUIP / UNLOAD_OLD / LOAD_NEW / RETURN_OLD / DONE)</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StepName { get; set; }

    /// <summary>해당 단계에서 사용한 AMR 슬롯 (STEP_COMPLETE 30/40/50 필수)</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CarrierSlot { get; set; }
}
