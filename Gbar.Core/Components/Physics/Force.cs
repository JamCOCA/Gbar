namespace Gbar.Core.Components.Physics;

/// <summary>
/// 本 tick 的累积力（定点牛顿，缩放 1:100），向量方向相对于实体当前所在坐标域。
/// 各受力系统（引力/推力/阻力）按固定系统顺序只做加法写入，
/// 由 ForceIntegrationSystem 统一积分进速度并清零，实体间并行安全。
/// </summary>
public struct Force
{
    public long X;
    public long Y;
    public long Z;
}
