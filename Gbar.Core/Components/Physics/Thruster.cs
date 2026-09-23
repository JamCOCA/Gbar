namespace Gbar.Core.Components.Physics;

/// <summary>
/// 推进器：推力向量 + 燃料。推力为定点牛顿（1:100），方向相对实体当前坐标域；
/// 每 tick 由 ThrustSystem 把 F/TickRate 作为冲量入池，并按 BurnRate/TickRate 扣燃料。
/// Fuel ≤ 0 时停止输出；BurnRate = 0 表示无限燃料。
/// </summary>
public struct Thruster
{
    public long ThrustX;
    public long ThrustY;
    public long ThrustZ;

    /// <summary>剩余燃料（定点 kg，1:100）。</summary>
    public long Fuel;

    /// <summary>每秒燃料消耗（定点 kg/s，1:100）；0 = 不消耗。</summary>
    public long BurnRate;
}
