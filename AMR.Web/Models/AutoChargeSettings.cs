namespace AMR.Web.Models;

/// <summary>
/// 자동 충전(AUTO CHARGE) 설정 — appsettings.json "AutoChargeSettings" 섹션에 영속화.
/// 섹션이 없으면(최초 실행) 아래 기본값(N1001, 20초)이 사용되고,
/// 사용자가 /AutoCharge 에서 값을 바꾸면 저장되어 재시작 후에도 그대로 이어진다.
/// </summary>
public class AutoChargeSettings
{
    /// <summary>자동 충전 기능 활성화</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Idle 판정 시간(초)</summary>
    public int IdleTimeoutSeconds { get; set; } = 20;

    /// <summary>충전 목적지 NodeId</summary>
    public string ChargeNodeId { get; set; } = "N1001";
}
