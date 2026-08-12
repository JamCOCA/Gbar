using Arch.Core;
using Arch.System;

namespace Gbar.Core.Systems;

/// <summary>
/// 定步长系统基类：构造时自动注册进 <see cref="FixedUpdateSystemGroup"/>，
/// 并从组获取 World 与命令缓冲。并行查询所需的 Hub 通过 <see cref="Commands"/> 静态字段提供。
/// </summary>
public abstract class FixedSystem : BaseSystem<World, float>
{
    protected static CommandBufferHub Commands = null!;

    protected FixedSystem(GameServer server) : base(server.World)
    {
        Commands = server.FixedSystems.Commands;
        server.FixedSystems.Add(this);
    }
}

/// <summary>
/// 变步长系统基类：构造时自动注册进 <see cref="UpdateSystemGroup"/>，
/// 并从组获取 World 与命令缓冲。并行查询所需的 Hub 通过 <see cref="Commands"/> 静态字段提供。
/// </summary>
public abstract class UpdateSystem : BaseSystem<World, float>
{
    protected static CommandBufferHub Commands = null!;

    protected UpdateSystem(GameServer server) : base(server.World)
    {
        Commands = server.UpdateSystems.Commands;
        server.UpdateSystems.Add(this);
    }
}
