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

    // ===== EXCHANGE 시나리오 확장 (docs/ACS-AMR_mqtt_exchangecmd.md) =====

    /// <summary>ACS Exchange Job ID — exchangeCmd/actionCmd/cancelCmd 공통, 모든 보고에 그대로 반환</summary>
    public string? JobId { get; set; }

    /// <summary>[exchangeCmd] 신규 매거진 픽업 위치 NodeId (Loc→NodeId 변환은 ACS 담당)</summary>
    public string? LoadSourceNode { get; set; }

    /// <summary>[exchangeCmd] 대상 설비 NodeId</summary>
    public string? EquipNode { get; set; }

    /// <summary>[exchangeCmd] 기존 매거진 반납 위치 NodeId</summary>
    public string? UnloadDestNode { get; set; }

    /// <summary>[exchangeCmd] 신규 매거진 AMR 슬롯 (1|2, ACS 자동배정)</summary>
    public int LoadSlot { get; set; }

    /// <summary>[exchangeCmd] 회수 매거진 AMR 슬롯 (3|4, ACS 자동배정)</summary>
    public int UnloadSlot { get; set; }

    /// <summary>[exchangeCmd] 픽업지 포트 유형 (기본 MATERIAL)</summary>
    public string? LoadSourcePortType { get; set; }

    /// <summary>[exchangeCmd] 반납지 포트 유형 (기본 MATERIAL)</summary>
    public string? UnloadDestPortType { get; set; }

    /// <summary>[actionCmd] 게이트 허가 유형 — UNLOAD(취출 허가) / LOAD(투입 허가)</summary>
    public string? Type { get; set; }

    /// <summary>[cancelCmd] 적재 후 취소 시 복귀 노드 (생략 시 자동충전 노드)</summary>
    public string? ReturnNode { get; set; }
}
