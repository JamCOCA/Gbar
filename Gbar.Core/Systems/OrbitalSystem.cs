using Arch.Core;
using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components.Celestial;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Maths;

namespace Gbar.Core.Systems;

/// <summary>
/// 轨道系统（定步长，Order 10）：按 <see cref="Orbit"/> 根数历元解析推进天体公转位置
/// （M = 2π·t/T + InitialPhase，牛顿迭代解开普勒方程，double 算转回 long），
/// 并按历元解析推进全部天体自转角（Rotation = InitialRotation + AngularVelocity × tick）。
/// 位置/自转都是 tick 的纯函数，不累积、零漂移，与客户端按同一公式求值（GbarNet.md 逐 bit 一致口径）。
/// 开普勒求解只用 +−×÷（<see cref="DetTrig"/>，不用超越函数），保证跨平台确定。
/// 必须排在管线最前：引力方向与 SOI 切换都依赖本 tick 的行星位置。
/// 父天体位置沿 Orbit.Parent 链递归解析（深度 ≤ 4），只读根天体的 GlobalPosition
/// （根天体无 Orbit、位置不被任何查询写），并行只读安全、无调度顺序依赖。
/// </summary>
[SystemOrder(10)]
public sealed partial class OrbitalSystem : FixedSystem
{
    /// <summary>开普勒方程牛顿迭代的固定次数（固定次数保证确定性；e &lt; 0.8 时 4~5 次即收敛到双精度极限）。</summary>
    private const int KeplerIterations = 6;

    public OrbitalSystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        OrbitQuery(World);
        SpinQuery(World);
    }

    /// <summary>行星/卫星：推进公转位置 + 自转角。</summary>
    [Query(Parallel = true)]
    [All<Celestial, Orbit, GlobalPosition>]
    private static void Orbit(ref Celestial body, in Orbit orbit, ref GlobalPosition pos)
    {
        var tick = Server.Tick;
        body.Rotation = RotationAt(in body, tick);
        var (x, y, z) = SolveAbsolute(in orbit, tick);
        pos.X = x;
        pos.Y = y;
        pos.Z = z;
    }

    /// <summary>恒星等不挂轨道的天体：只推进自转角，位置固定。</summary>
    [Query(Parallel = true)]
    [All<Celestial, GlobalPosition>]
    [None<Orbit>]
    private static void Spin(ref Celestial body)
    {
        body.Rotation = RotationAt(in body, Server.Tick);
    }

    /// <summary>历元解析自转角：Rotation = InitialRotation + AngularVelocity × tick，折回 [0, 2π)。</summary>
    private static float RotationAt(in Celestial body, long tick)
        => (float)DetTrig.WrapPhase(body.InitialRotation + (double)body.AngularVelocity * tick);

    /// <summary>绝对位置 = 父天体绝对位置 + 本轨道偏移；父链沿根数递归解析，根天体（恒星）读其静止的 GlobalPosition。</summary>
    private static (long X, long Y, long Z) SolveAbsolute(in Orbit orbit, long tick)
    {
        long px = 0, py = 0, pz = 0;
        var parent = orbit.Parent;
        if (parent != Entity.Null)
        {
            if (Server.World.TryGet<Orbit>(parent, out var parentOrbit))
            {
                (px, py, pz) = SolveAbsolute(in parentOrbit, tick);
            }
            else
            {
                // 父实体既无 Orbit 也无 GlobalPosition：Parent 引用已失效（如读到旧存档的错位 Entity）。
                // 不能 World.Get 硬取——缺组件时 Arch 在工作线程抛 NRE 直接打死进程；告警并按原点降级。
                if (Server.World.TryGet<GlobalPosition>(parent, out var pp))
                {
                    px = pp.X;
                    py = pp.Y;
                    pz = pp.Z;
                }
                else
                {
                    Logging.Log.Warn($"OrbitalSystem：实体 {parent} 缺少 GlobalPosition，其 Orbit.Parent 引用已失效，按原点处理");
                }
            }
        }

        var (ox, oy, oz) = SolveLocalOffset(in orbit, tick);
        return (px + ox, py + oy, pz + oz);
    }

    /// <summary>轨道局部偏移（米→定点）：解开普勒方程得偏近点角 E，x = a(cosE − e)，y = a·√(1−e²)·sinE，轨道面取 XY 平面（Z = 0）。</summary>
    private static (long X, long Y, long Z) SolveLocalOffset(in Orbit orbit, long tick)
    {
        if (orbit.OrbitalPeriod <= 0 || orbit.Eccentricity < 0 || orbit.Eccentricity >= 1)
            return (0, 0, 0);   // 非法根数：跳过推进，避免 NaN 经 Fix.FromDouble 静默污染位置

        var t = tick / (double)Server.TickRate;   // 秒
        var meanAnomaly = DetTrig.TwoPi * t / orbit.OrbitalPeriod + orbit.InitialPhase;
        var e = orbit.Eccentricity;
        var ea = SolveEccentricAnomaly(meanAnomaly, e);
        var a = orbit.SemiMajorAxis;
        var x = a * (DetTrig.Cos(ea) - e);
        var y = a * Math.Sqrt(1 - e * e) * DetTrig.Sin(ea);
        return (Fix.FromDouble(x), Fix.FromDouble(y), 0);
    }

    /// <summary>牛顿迭代解开普勒方程 M = E − e·sinE：只用 +−×÷（<see cref="DetTrig"/>），固定迭代次数保证确定。</summary>
    private static double SolveEccentricAnomaly(double meanAnomaly, double e)
    {
        var m = meanAnomaly - DetTrig.TwoPi * Math.Floor(meanAnomaly / DetTrig.TwoPi + 0.5);   // → [−π, π]
        var ea = e < 0.8 ? m : DetTrig.Pi;   // 高偏心率从 π 起步更稳
        for (var i = 0; i < KeplerIterations; i++)
            ea -= (ea - e * DetTrig.Sin(ea) - m) / (1 - e * DetTrig.Cos(ea));
        return ea;
    }
}
