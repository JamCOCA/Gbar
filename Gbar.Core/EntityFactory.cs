using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using Arch.Core;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Input;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Net;
using Gbar.Net.Protocol.Components;
using Gbar.Net.Protocol.Entities;

namespace Gbar.Core;

/// <summary>
/// 实体工厂：封装常见实体的组件装配，避免调用方手写一长串 world.Create(...)。
/// 通过 <see cref="GameServer.EntityFactory"/> 暴露，供系统与调试注入点复用。
/// 除玩家外均自动分配 NetId（玩家 NetId 由会话管理，见 <see cref="CreatePlayer"/>）。
/// 所有创建都应在主循环线程串行进行（与 World 的读写约定一致）。
/// </summary>
public sealed class EntityFactory
{
    private readonly World _world;
    private readonly ClientRegistry _registry;

    public EntityFactory(World world, ClientRegistry registry)
    {
        _world = world;
        _registry = registry;
    }

    /// <summary>创建玩家实体（PlayerTag + 原点出生 + 零速度）。NetId 由调用方分配并传入（会话需记录该值）。</summary>
    public Entity CreatePlayer(int netId) => _world.Create(
        new NetworkId { Value = netId },
        new PlayerTag(),
        new EntityTypeTag { Value = EntityType.Player },
        new Position(),
        new Velocity(),
        new Rotation(),
        new MoveInput(),
        new Health { Current = 100, Max = 100 },
        new Cpc { Current = 50, Max = 50 },
        new AoiCell());

    /// <summary>创建地表游荡敏化人（带速度的运动实体，测试运动学移动与增量同步）。自动分配 NetId。</summary>
    public Entity CreateSurfaceWanderer(in Position position, in Velocity velocity) => _world.Create(
        new NetworkId { Value = _registry.AllocNetId() },
        new EntityTypeTag { Value = EntityType.SurfaceWanderer },
        position,
        velocity,
        new Health { Current = 80, Max = 80 },
        new Cpc { Current = 30, Max = 30 },
        new AoiCell());

    /// <summary>创建静态实体（资源点/建筑等，无速度）。自动分配 NetId。</summary>
    public Entity CreateStaticEntity(EntityType type, in Position position) => _world.Create(
        new NetworkId { Value = _registry.AllocNetId() },
        new EntityTypeTag { Value = type },
        position,
        new AoiCell());
}
