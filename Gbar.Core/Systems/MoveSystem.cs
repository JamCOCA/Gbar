using Arch.Core;
using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components;
using Gbar.Core.Logging;

namespace Gbar.Core.Systems;

/// <summary>
/// 移动系统：两域各自的整数加法移动，速度量纲为定点单位/tick。
/// 全局域（SOI 外深空）与局部域（某天体 SOI 内）组件互斥，两个查询互不干扰。
/// 本系统是唯一的位置积分点，排在所有改写速度的系统（引力/推力/阻力/力积分）之后。
/// 查询由源码生成器并行调度；<see cref="Update"/> 仅做每 tick 一次的聚合统计与日志。
/// </summary>
[SystemOrder(50)]
public sealed partial class MoveSystem : FixedSystem
{
    private static readonly QueryDescription _globalCountQuery = new QueryDescription().WithAll<GlobalPosition, GlobalVelocity>();
    private static readonly QueryDescription _localCountQuery = new QueryDescription().WithAll<LocalPosition, LocalVelocity>();

    public MoveSystem(GameServer server) : base(server) { }

    /// <summary>base.Update 会执行所有 [Query] 方法（含并行），返回后统计与日志在主线程安全执行。</summary>
    public override void Update(in float delta)
    {
        base.Update(in delta);
        Log.Debug($"MoveSystem 全局域 {World.CountEntities(in _globalCountQuery)} 个实体，局部域 {World.CountEntities(in _localCountQuery)} 个实体");
    }

    [Query(Parallel = true)]
    [All<GlobalPosition, GlobalVelocity>]
    private static void MoveGlobal(ref GlobalPosition position, in GlobalVelocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;
        position.Z += velocity.Z;
    }

    [Query(Parallel = true)]
    [All<LocalPosition, LocalVelocity>]
    private static void MoveLocal(ref LocalPosition position, in LocalVelocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;
        position.Z += velocity.Z;
    }
}
