using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using Arch.Core;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Input;
using Gbar.Core.Components.Physics;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Net;
using Gbar.Net.Protocol.Components;
using Gbar.Net.Protocol.Entities;
using Gbar.Physics;

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

    /// <summary>创建玩家实体（PlayerTag + 出生点 + 零速度，Kinematic 胶囊刚体承载移动）。NetId 由调用方分配并传入（会话需记录该值）。</summary>
    public Entity CreatePlayer(int netId) => _world.Create(
        new NetworkId { Value = netId },
        new PlayerTag(),
        new EntityTypeTag { Value = EntityType.Player },
        new Position { X = 0.0, Y = 0.95, Z = 0.0 },   // 胶囊半高(0.55+0.35)，中心抬到地面之上
        new Velocity(),
        new Rotation(),
        new MoveInput(),
        new MoveIntent(),
        new PhysicsBody(PhysicsBodyType.Kinematic, PhysicsShape.Capsule(0.35f, 0.55f)),
        new Health { Current = 100, Max = 100 },
        new Cpc { Current = 50, Max = 50 },
        new AoiCell());

    /// <summary>创建地表游荡敏化人（Kinematic 胶囊刚体 + 持续移动意图，测试物理驱动移动与增量同步）。自动分配 NetId。</summary>
    public Entity CreateSurfaceWanderer(in Position position, in MoveIntent intent) => _world.Create(
        new NetworkId { Value = _registry.AllocNetId() },
        new EntityTypeTag { Value = EntityType.SurfaceWanderer },
        position,
        new Velocity(),
        new Rotation(),
        intent,
        new PhysicsBody(PhysicsBodyType.Kinematic, PhysicsShape.Capsule(0.35f, 0.55f)),
        new Health { Current = 80, Max = 80 },
        new Cpc { Current = 30, Max = 30 },
        new AoiCell());

    /// <summary>创建静态实体（资源点/建筑等，无速度）。自动分配 NetId。</summary>
    public Entity CreateStaticEntity(EntityType type, in Position position) => _world.Create(
        new NetworkId { Value = _registry.AllocNetId() },
        new EntityTypeTag { Value = type },
        position,
        new AoiCell());

    /// <summary>创建物理刚体实体（动态体，受重力/碰撞，位姿由 PhysicsSystem 回写同步）。自动分配 NetId。</summary>
    public Entity CreatePhysicsBody(EntityType type, in Position position, in PhysicsShape shape, float mass = 1f) => _world.Create(
        new NetworkId { Value = _registry.AllocNetId() },
        new EntityTypeTag { Value = type },
        position,
        new Velocity(),
        new Rotation(),
        new PhysicsBody(PhysicsBodyType.Dynamic, shape, mass),
        new AoiCell());

    /// <summary>创建静态碰撞体实体（不参与动力学，仅作碰撞阻挡，如建筑/地形）。自动分配 NetId。</summary>
    public Entity CreateStaticCollider(EntityType type, in Position position, in PhysicsShape shape) => _world.Create(
        new NetworkId { Value = _registry.AllocNetId() },
        new EntityTypeTag { Value = type },
        position,
        new PhysicsStatic(shape),
        new AoiCell());
}
