namespace AMR.Models;

/// <summary>
/// 모델별 LOAD / UNLOAD offset 보정값 (노드·포트 무관 공통값).
///
/// 배경:
///   ACS 가 moveCmd 에 Model 정보를 실어 보내면, 그 모델의 LOADING/UNLOADING 작업
///   위치에 일정한 보정이 필요할 수 있다. PortOffset(노드+슬롯별) 위에 추가로 더해진다.
///   ACS·Fairino 무수정 — AMR.Web 에서 QR offset 전달 직전에 합산.
///
/// 단위: dx/dy = mm, drz = degree.
/// 한 모델당 한 행, LOAD 와 UNLOAD 각각의 offset 보유.
/// </summary>
public record ModelOffset
{
    /// <summary>모델 이름 (ACS 가 보내는 Model 값과 매칭)</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>LOAD 작업 시 dx 보정 (mm)</summary>
    public double LoadDx { get; init; }
    /// <summary>LOAD 작업 시 dy 보정 (mm)</summary>
    public double LoadDy { get; init; }
    /// <summary>LOAD 작업 시 drz 보정 (degree)</summary>
    public double LoadDrz { get; init; }

    /// <summary>UNLOAD 작업 시 dx 보정 (mm)</summary>
    public double UnloadDx { get; init; }
    /// <summary>UNLOAD 작업 시 dy 보정 (mm)</summary>
    public double UnloadDy { get; init; }
    /// <summary>UNLOAD 작업 시 drz 보정 (degree)</summary>
    public double UnloadDrz { get; init; }
}
