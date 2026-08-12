using Arch.Core;
using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components;

using Gbar.Core.Logging;

namespace Gbar.Core.Systems;

/// <summary>
/// 移动系统：按速度推进所有实体的位置，越界实体经命令缓冲延迟销毁。
/// 继承 <see cref="FixedSystem"/> 构造即自动注册进 fixed 组，
/// 并行查询按 chunk 分发到多线程执行，结构变更由组统一回放。
/// </summary>
[SystemOrder(0)]
public sealed partial class MovementSystem : FixedSystem
{
    /// <summary>世界边界，坐标绝对值超过该值的实体将被销毁。</summary>
    private const float Bound = 1000f;

    public MovementSystem(GameServer server) : base(server) { }

    [Query(Parallel = true)]
    [All<Position, Velocity>]
    private static void Move([Data] in float delta, Entity entity, ref Position position, in Velocity velocity)
    {
        position.X += velocity.X * delta;
        position.Y += velocity.Y * delta;

    }
}
