using Arch.Core;
using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components;
using Gbar.Core.Logging;

namespace Gbar.Core.Systems;

/// <summary>
/// 移动系统：按速度推进所有实体的位置。
/// 查询由源码生成器并行调度；<see cref="Update"/> 仅做每 tick 一次的聚合统计与日志。
/// </summary>
[SystemOrder(0)]
public sealed partial class MovementSystem : FixedSystem
{
    private static readonly QueryDescription _countQuery = new QueryDescription().WithAll<Position, Velocity>();

    public MovementSystem(GameServer server) : base(server) { }

    /// <summary>base.Update 会执行所有 [Query] 方法（含并行），返回后统计与日志在主线程安全执行。</summary>
    public override void Update(in float delta)
    {
        base.Update(in delta);
        Log.Debug($"MovementSystem 处理 {World.CountEntities(in _countQuery)} 个实体 (delta={delta:F4})");
    }

    [Query(Parallel = true)]
    [All<Position, Velocity>]
    private static void Move([Data] in float delta, ref Position position, in Velocity velocity)
    {
        position.X += velocity.X * delta;
        position.Y += velocity.Y * delta;
    }
}
