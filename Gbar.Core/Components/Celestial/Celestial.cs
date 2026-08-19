namespace Gbar.Core.Components.Celestial;

/// <summary>
/// 天体引力与自转身份：恒星和行星都挂。星体的"质量"以 <see cref="Mu"/>（= G·M）形式存在，
/// 引力公式只需引力源的 μ 与受力方的 Mass。天体不挂 Mass，不做力积分。
/// </summary>
public struct Celestial
{
    /// <summary>引力参数 μ = G·M（m³/s²）。</summary>
    public double Mu;

    /// <summary>地表半径（米）。</summary>
    public double SurfaceRadius;

    /// <summary>
    /// 引力圈半径（米），加载时沿 Orbit.Parent 链按 r = a·(μ/μ_母)^(2/5) 自顶向下算好缓存；
    /// 恒星无母天体，取星系边界（double.MaxValue 或星图半径）。
    /// </summary>
    public double SoiRadius;

    /// <summary>当前自转角（弧度）。</summary>
    public float Rotation;

    /// <summary>自转角速度（rad/tick）。</summary>
    public float AngularVelocity;
}
