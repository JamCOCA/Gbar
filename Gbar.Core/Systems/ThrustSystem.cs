using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components.Physics;

namespace Gbar.Core.Systems;

/// <summary>
/// 推力系统（定步长，Order 30）：读推进器推力向量，每 tick 把 F/TickRate 作为冲量入池，
/// 并按 BurnRate/TickRate 扣燃料；燃料耗尽（Fuel ≤ 0）停止输出。
/// 查询带 Mass 过滤，确保只给可积分实体写入冲量（避免写入永不兑换的冲量）。
/// 限速等玩法规则后续在本系统扩展。
/// </summary>
[SystemOrder(30)]
public sealed partial class ThrustSystem : FixedSystem
{
    public ThrustSystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        ThrustQuery(World);
    }

    [Query(Parallel = true)]
    [All<Thruster, Mass, Impulse>]
    private static void Thrust(ref Thruster thruster, ref Impulse impulse, in Mass mass)
    {
        if (thruster.Fuel <= 0) return;

        var r = Server.TickRate;
        impulse.X += thruster.ThrustX / r;
        impulse.Y += thruster.ThrustY / r;
        impulse.Z += thruster.ThrustZ / r;

        if (thruster.BurnRate > 0)
            thruster.Fuel -= thruster.BurnRate / r;
    }
}
