namespace AMR.Web.Models;

/// <summary>
/// 자동 충전(AUTO CHARGE) 설정 — appsettings.json "AutoChargeSettings" 섹션에 영속화.
/// 섹션이 없으면(최초 실행) 아래 기본값(N1001, 20초)이 사용되고,
/// 사용자가 /AutoCharge 에서 값을 바꾸면 저장되어 재시작 후에도 그대로 이어진다.
/// </summary>
public class AutoChargeSettings
{
    /// <summary>자동 충전 기능 활성화 — v0.3.1: 기본 false.
    /// ACS 운영에서는 유휴 시 자동 충전 이동이 ACS 배차와 경합하므로(거부/유휴 직후 충전소 이동으로 관찰됨)
    /// 기본 끔. 필요 시 /AutoCharge 화면 또는 appsettings 에서 명시적으로 켠다.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Idle 판정 시간(초)</summary>
    public int IdleTimeoutSeconds { get; set; } = 20;

    /// <summary>충전 목적지 NodeId</summary>
    public string ChargeNodeId { get; set; } = "N1001";
}
