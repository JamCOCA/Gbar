using Arch.Core;
using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components.Celestial;
using Gbar.Core.Components.Physics;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Maths;

namespace Gbar.Core.Systems;

/// <summary>
/// 引力系统（定步长，Order 20）：向飞行实体的 <see cref="Impulse"/> 池加入本 tick 引力冲量
/// J = μ·m / (d²·R)（R = TickRate），方向朝唯一引力源。
/// 局部域引力源 = ParentRef 行星（μ 取父天体 Celestial，并行只读安全）；全局域引力源 = 母恒星。
/// double 算、<see cref="Fix.FromDouble"/> 转回定点。着地实体无 <see cref="Mass"/>，天然不匹配。
/// 全局域引力源是唯一的母恒星（单星假设）：位置与 μ 均静态，解析一次后缓存。
/// </summary>
[SystemOrder(20)]
public sealed partial class GravitySystem : FixedSystem
{
    private static readonly QueryDescription _starQuery =
        new QueryDescription().WithAll<Celestial, GlobalPosition>().WithNone<Orbit>();

    // 母恒星（全局域唯一引力源）：μ 与位置静态，解析一次缓存，并行查询内只读
    private static double _starMu;
    private static GlobalPosition _starPos;
    private static bool _starResolved;

    public GravitySystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        if (!_starResolved)
            ResolveStar();

        GravityGlobalQuery(World);
        GravityLocalQuery(World);
    }

    /// <summary>解析全局域唯一引力源（母恒星 = 无 Orbit 的天体），缓存其静态 μ 与位置。</summary>
    private static void ResolveStar()
    {
        var world = Server.World;
        var count = world.CountEntities(in _starQuery);
        if (count == 0) return;

        var entities = new Entity[count];
        world.GetEntities(in _starQuery, entities.AsSpan());
        var star = entities[0];
        _starMu = world.Get<Celestial>(star).Mu;
        _starPos = world.Get<GlobalPosition>(star);
        _starResolved = true;
    }

    /// <summary>全局域：引力源 = 母恒星（位置/μ 静态缓存）。</summary>
    [Query(Parallel = true)]
    [All<GlobalPosition, Mass, Impulse>]
    private static void GravityGlobal(ref Impulse impulse, in GlobalPosition pos, in Mass mass)
    {
        var (ix, iy, iz) = GravityImpulse(pos.X, pos.Y, pos.Z, _starPos.X, _starPos.Y, _starPos.Z, _starMu, mass.Value);
        impulse.X += ix;
        impulse.Y += iy;
        impulse.Z += iz;
    }

    /// <summary>局部域：引力源 = Parent 行星（行星心 = 局部原点）。</summary>
    [Query(Parallel = true)]
    [All<LocalPosition, ParentRef, Mass, Impulse>]
    private static void GravityLocal(ref Impulse impulse, in LocalPosition pos, in ParentRef parent, in Mass mass)
    {
        var celestial = Server.World.Get<Celestial>(parent.Parent);
        var (ix, iy, iz) = GravityImpulse(pos.X, pos.Y, pos.Z, 0, 0, 0, celestial.Mu, mass.Value);
        impulse.X += ix;
        impulse.Y += iy;
        impulse.Z += iz;
    }

    /// <summary>引力冲量：J = μ·m/(d²·R)，方向朝引力源；double 算，Fix.FromDouble 转回定点。</summary>
    private static (long X, long Y, long Z) GravityImpulse(
        long px, long py, long pz, long sx, long sy, long sz, double mu, long massFix)
    {
        var dx = (double)(sx - px);
        var dy = (double)(sy - py);
        var dz = (double)(sz - pz);
        var d2 = dx * dx + dy * dy + dz * dz;
        if (d2 <= 0) return (0, 0, 0);   // 与引力源重合：无方向，跳过

        var d = Math.Sqrt(d2);
        var scale = (double)Fix.Scale;
        var j = mu * (massFix / scale) / ((d / scale) * (d / scale) * Server.TickRate);

        return (Fix.FromDouble(j * dx / d), Fix.FromDouble(j * dy / d), Fix.FromDouble(j * dz / d));
    }
}
