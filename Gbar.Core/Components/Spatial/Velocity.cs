namespace Gbar.Core.Components.Spatial;

/// <summary>
/// 实体速度（米/秒）。由 MovementSystem 以 kinematic 方式积分（pos += vel·Δt）。
/// 没有此组件的实体视为静止（如静态资源点/建筑）。
/// </summary>
public struct Velocity
{
    public double X;
    public double Y;
    public double Z;
}
