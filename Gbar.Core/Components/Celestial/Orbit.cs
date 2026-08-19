using Arch.Core;

namespace Gbar.Core.Components.Celestial;

/// <summary>
/// 公转轨道根数：只有行星（及卫星）挂，恒星是轨道中心不挂。
/// OrbitalSystem 按 M = 2π·t/T + InitialPhase 解开普勒方程解析求位置，
/// 星体间引力永不积分，轨道由根数预定义、零漂移。
/// </summary>
public struct Orbit
{
    /// <summary>母天体实体（恒星；卫星则为所属行星）。</summary>
    public Entity Parent;

    /// <summary>半长轴 a（米）。</summary>
    public double SemiMajorAxis;

    /// <summary>偏心率 e（圆轨道 = 0，退化为匀速圆周）。</summary>
    public double Eccentricity;

    /// <summary>公转周期（秒）。</summary>
    public double OrbitalPeriod;

    /// <summary>历元相位（初始平近点角，弧度）。</summary>
    public double InitialPhase;
}
