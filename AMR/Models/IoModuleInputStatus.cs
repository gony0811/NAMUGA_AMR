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

    /// <summary>X002 — MZ DETECT 1</summary>
    public bool MzDetect1 { get; set; }

    /// <summary>X003 — MZ DETECT 2</summary>
    public bool MzDetect2 { get; set; }

    /// <summary>X004 — MZ DETECT 3</summary>
    public bool MzDetect3 { get; set; }

    /// <summary>X005 — MZ DETECT 4</summary>
    public bool MzDetect4 { get; set; }
}
