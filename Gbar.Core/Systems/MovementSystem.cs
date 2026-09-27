using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Net.Protocol.Components;

namespace Gbar.Core.Systems;

/// <summary>
/// 移动系统（定步长，Order 60）：运动学积分，唯一的位置写入点。
/// 位置 = 位置 + 速度 × Δt（Δt 为固定步长），不再区分坐标域，也不涉及冲量/质量/引力。
/// </summary>
[SystemOrder(60)]
public sealed partial class MovementSystem : FixedSystem
{
    private static float _delta;

    public MovementSystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        _delta = delta;
        MoveQuery(World);
    }

    [Query(Parallel = true)]
    [All<Position, Velocity>]
    private static void Move(ref Position position, in Velocity velocity)
    {
        position.X += velocity.X * _delta;
        position.Y += velocity.Y * _delta;
        position.Z += velocity.Z * _delta;
    }
}
