using Arch.Core;
using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Logging;
using Gbar.Core.Maths;

namespace Gbar.Core.Systems;

/// <summary>
/// 移动系统：两域各自的整数加法移动。速度量纲为 cm/tick（定点缩放 1:100，即 m/s × 500），
/// 位置量纲为 cm（m × 100），故每 tick 位移 = velocity / Scale（整除，位置量子 1 cm）。
/// 全局域（SOI 外深空）与局部域（某天体 SOI 内）组件互斥，两个查询互不干扰。
/// 本系统是唯一的位置积分点，排在所有改写速度的系统（引力/推力/阻力/力积分）之后。
/// </summary>
[SystemOrder(60)]
public sealed partial class MoveSystem : FixedSystem
{
    private static readonly QueryDescription _globalCountQuery = new QueryDescription().WithAll<GlobalPosition, GlobalVelocity>();
    private static readonly QueryDescription _localCountQuery = new QueryDescription().WithAll<LocalPosition, LocalVelocity>();

    public MoveSystem(GameServer server) : base(server) { }

    /// <summary>手动调用源码生成器生成的查询入口（并行），返回后统计与日志在主线程安全执行。</summary>
    public override void Update(in float delta)
    {
        MoveGlobalQuery(World);
        MoveLocalQuery(World);
        Log.Debug($"MoveSystem 全局域 {World.CountEntities(in _globalCountQuery)} 个实体，局部域 {World.CountEntities(in _localCountQuery)} 个实体");
    }

    [Query(Parallel = true)]
    [All<GlobalPosition, GlobalVelocity>]
    private static void MoveGlobal(ref GlobalPosition position, in GlobalVelocity velocity)
    {
        position.X += velocity.X / Fix.Scale;
        position.Y += velocity.Y / Fix.Scale;
        position.Z += velocity.Z / Fix.Scale;
    }

    [Query(Parallel = true)]
    [All<LocalPosition, LocalVelocity>]
    private static void MoveLocal(ref LocalPosition position, in LocalVelocity velocity)
    {
        position.X += velocity.X / Fix.Scale;
        position.Y += velocity.Y / Fix.Scale;
        position.Z += velocity.Z / Fix.Scale;
    }
}
