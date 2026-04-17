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

    /// <summary>포트 유형 (FACILITY=설비포트, MATERIAL=자재포트)</summary>
    public string? PortType { get; set; }

    /// <summary>AMR 슬롯 번호 (1~4)</summary>
    public int AmrSlot { get; set; } = 1;
}
