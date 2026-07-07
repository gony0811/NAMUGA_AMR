namespace AMR.Models;

/// <summary>
/// LS XEL-BSSRT I/O 모듈 입력 상태 (Discrete Input X000~X005)
/// </summary>
public class IoModuleInputStatus
{
    /// <summary>X000 — EMO (비상정지, true=활성)</summary>
    public bool Emo { get; set; }

    /// <summary>X001 — RESET 스위치</summary>
    public bool Reset { get; set; }

    /// <summary>AMR 포트 1 매거진 감지 — 실제 배선: X005</summary>
    public bool MzDetect1 { get; set; }

    /// <summary>AMR 포트 2 매거진 감지 — 실제 배선: X004</summary>
    public bool MzDetect2 { get; set; }

    /// <summary>AMR 포트 3 매거진 감지 — 실제 배선: X003</summary>
    public bool MzDetect3 { get; set; }

    /// <summary>AMR 포트 4 매거진 감지 — 실제 배선: X002</summary>
    public bool MzDetect4 { get; set; }
}
