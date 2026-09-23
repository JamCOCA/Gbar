namespace Gbar.Core.Components.Physics;

/// <summary>
/// 冲量池（定点 N·s，缩放 1:100），向量方向相对于实体当前所在坐标域。
/// 受力系统（引力/推力/阻力/碰撞）按固定系统顺序只做加法写入；
/// 由 ImpulseIntegrationSystem 统一兑换成速度，未凑够一个速度量子的残差留池不清零（见 GbarMotion.md）。
/// </summary>
public struct Impulse
{
    public long X;
    public long Y;
    public long Z;
}
