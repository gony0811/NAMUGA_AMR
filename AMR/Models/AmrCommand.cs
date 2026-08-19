namespace AMR.Models;

/// <summary>
/// ACS에서 MQTT를 통해 수신하는 명령 (amr/{ClientId}/command)
/// </summary>
public class AmrCommand
{
    /// <summary>명령 일련번호 (년월일_시분초_일련번호)</summary>
    public string CmdId { get; set; } = string.Empty;

    /// <summary>명령 종류 (moveCmd, actionCmd)</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>명령 대상 노드 ID (ex: N0001)</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>포트 위치 (LEFT, RIGHT)</summary>
    public string? Port { get; set; }

    /// <summary>목적지에서 수행할 작업 (LOAD, UNLOAD, EXCHANGE)</summary>
    public string? JobType { get; set; }

    /// <summary>포트 유형 — "EQP" 문자열 포함 시 설비포트, 그 외(예: MATERIAL)는 자재포트</summary>
    public string? PortType { get; set; }

    /// <summary>AMR 슬롯 번호 (1~4)</summary>
    public int AmrSlot { get; set; } = 1;

    /// <summary>모델 정보 (ACS 전달) — 모델별 LOAD/UNLOAD offset 보정에 사용</summary>
    public string? Model { get; set; }

    // ===== EXCHANGE 확장 (docs/ACS-AMR_mqtt_exchange_v0.3.docx) =====

    /// <summary>ACS Job ID — actionCmd/cancelCmd 에서 진행 중 job 과 대조. reply 에 그대로 반환</summary>
    public string? JobId { get; set; }

    /// <summary>[actionCmd] 이번 액션 유형 — UNLOAD(기존 매거진 취출, 회수슬롯 PLACE) / LOAD(신규 매거진 투입, 투입슬롯 PICK).
    /// PICK/PLACE 결정에 type 이 있으면 type 우선, 없으면 jobType 사용</summary>
    public string? Type { get; set; }
}
