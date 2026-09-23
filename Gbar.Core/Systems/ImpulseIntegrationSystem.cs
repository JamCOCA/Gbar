using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components.Physics;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Maths;

namespace Gbar.Core.Systems;

/// <summary>
/// 冲量积分系统（定步长，Order 50）：唯一积分点。Δv = p/m
/// （Δv_fix = p_fix × K / m_fix，K = Scale²/TickRate，Int128，截断），
/// 兑换后只扣已兑换的整量子部分，残差留池不清零（亚量子冲量逐 tick 累积，见 GbarMotion.md）。
/// 速度只由本系统写；受力系统（引力/推力/阻力）只往 Impulse 池做加法。
/// </summary>
[SystemOrder(50)]
public sealed partial class ImpulseIntegrationSystem : FixedSystem
{
    /// <summary>速度换算系数 K = Scale²/TickRate（对 20 tick/s 为 500），懒初始化。</summary>
    private static long _k = -1;

    public ImpulseIntegrationSystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        if (_k < 0)
            _k = Fix.Scale * Fix.Scale / Server.TickRate;

        IntegrateGlobalQuery(World);
        IntegrateLocalQuery(World);
    }

    [Query(Parallel = true)]
    [All<Impulse, Mass, GlobalVelocity>]
    private static void IntegrateGlobal(ref GlobalVelocity vel, ref Impulse impulse, in Mass mass)
    {
        vel.X += Exchange(ref impulse.X, mass.Value);
        vel.Y += Exchange(ref impulse.Y, mass.Value);
        vel.Z += Exchange(ref impulse.Z, mass.Value);
    }

    [Query(Parallel = true)]
    [All<Impulse, Mass, LocalVelocity>]
    private static void IntegrateLocal(ref LocalVelocity vel, ref Impulse impulse, in Mass mass)
    {
        vel.X += Exchange(ref impulse.X, mass.Value);
        vel.Y += Exchange(ref impulse.Y, mass.Value);
        vel.Z += Exchange(ref impulse.Z, mass.Value);
    }

    /// <summary>把单个冲量分量兑换成速度增量（截断），并只扣已兑换部分，残差留池。</summary>
    private static long Exchange(ref long impulse, long massFix)
    {
        var k = _k;
        var dv = (long)((Int128)impulse * k / massFix);
        impulse -= (long)((Int128)dv * massFix / k);
        return dv;
    }
}
