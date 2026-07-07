namespace AMR.Models;

/// <summary>
/// 노드(설비/자재 포트)별 + 슬롯(Port)별 QR offset 보정값.
///
/// 배경:
///   설비포트가 모두 같은 Fairino 티칭포지션을 공유하는데, QR 읽는 점과 실제
///   작업 점의 기하(거리)에 따라 카메라 측정에 노드/슬롯별 systematic bias 가 생긴다.
///   ACS·Fairino 를 건드리지 않고, AMR.Web 이 QR offset(dx,dy,drz)을 Fairino 로
///   전달하기 직전에 이 보정값을 더해서 흡수한다.
///
/// 단위: dx/dy = mm, drz = degree (전달 시 ×100 되어 0.01° 단위로 나감).
/// </summary>
public record PortOffset
{
    /// <summary>대상 노드 ID (예: N2001)</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>대상 슬롯 — "LEFT" / "RIGHT" (빈 값이면 모든 슬롯 공통)</summary>
    public string Port { get; init; } = string.Empty;

    /// <summary>dx 보정값 (mm) — QR dx 에 더해짐 (AI0)</summary>
    public double OffsetDx { get; init; }

    /// <summary>dy 보정값 (mm) — QR dy 에 더해짐 (AI1)</summary>
    public double OffsetDy { get; init; }

    /// <summary>drz 보정값 (degree) — QR drz 에 더해짐 (AI2, ×100)</summary>
    public double OffsetDrz { get; init; }
}
