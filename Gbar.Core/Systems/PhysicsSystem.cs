using System.Numerics;
using Arch.Core;
using Gbar.Core.Components.Physics;
using Gbar.Core.Physics;
using Gbar.Net.Protocol.Components;
using Gbar.Physics;

namespace Gbar.Core.Systems;

/// <summary>
/// 物理系统（定步长，Order 70，跑在 MoveInputSystem 之后、AoiSystem 之前）：
/// ECS 世界与物理世界（Gbar.Physics）之间的唯一通道。单线程分五阶段执行：
/// 1. SyncBodies/SyncStatics：按查询结果集 diff 惰性建体/删体（不直接 Create/Destroy ECS 实体）；
/// 2. ApplyMoveIntents：把 <see cref="MoveIntent"/> 翻译成刚体线速度（Dynamic/Kinematic 均适用，保留垂直速度）；
/// 3. ApplyCommands：消费一次性命令（<see cref="ApplyImpulse"/>/<see cref="TeleportRequest"/>），经命令缓冲移除组件；
/// 4. Step：推进 Bepu 模拟；
/// 5. WriteBackBodies：把 Bepu 位姿/速度回写到 Position/Rotation/Velocity（float→double 加宽）。
/// 其他系统只往实体上写意图/命令组件，不直接触碰物理。
/// 回写后的 Position/Velocity/Rotation 由后续 AoiSystem/NetSyncSystem 照常同步，客户端无感知。
/// </summary>
[SystemOrder(70)]
public sealed class PhysicsSystem : FixedSystem
{
    private static readonly QueryDescription _bodyQuery = new QueryDescription().WithAll<PhysicsBody>();
    private static readonly QueryDescription _staticQuery = new QueryDescription().WithAll<PhysicsStatic>();
    private static readonly QueryDescription _intentQuery = new QueryDescription().WithAll<PhysicsBody, MoveIntent>();
    private static readonly QueryDescription _impulseQuery = new QueryDescription().WithAll<PhysicsBody, ApplyImpulse>();
    private static readonly QueryDescription _teleportQuery = new QueryDescription().WithAll<PhysicsBody, TeleportRequest>();

    // 上一 tick 存活实体的句柄映射，用于 diff 出"已销毁/已移除组件"的实体并回收 Bepu 体。
    private readonly Dictionary<Entity, PhysicsBodyHandle> _trackedBodies = new();
    private readonly Dictionary<Entity, PhysicsStaticHandle> _trackedStatics = new();

    // 复用的临时集合，避免每 tick 分配。
    private readonly HashSet<Entity> _alive = new();
    private readonly List<Entity> _removeList = new();

    public PhysicsSystem(GameServer server) : base(server) { }

    public override void Initialize()
    {
        // 世界地面：静态大盒，顶面位于 y=0。后续可改为由静态 ECS 实体（PhysicsStatic）承载地形。
        Server.Physics.CreateStatic(
            PhysicsShape.Box(1000f, 0.5f, 1000f),
            new Vector3(0f, -0.5f, 0f),
            Quaternion.Identity);
    }

    public override void Update(in float delta)
    {
        var physics = Server.Physics;
        SyncBodies(physics);
        SyncStatics(physics);
        ApplyMoveIntents(physics);
        ApplyCommands(physics);
        physics.Step(delta);
        WriteBackBodies(physics);
    }

    /// <summary>应用持续移动意图（Dynamic/Kinematic 均适用）：水平速度按意图设置，垂直速度保留给重力/跳跃。</summary>
    private void ApplyMoveIntents(PhysicsWorld physics)
    {
        var world = Server.World;
        var count = world.CountEntities(in _intentQuery);
        var entities = new Entity[count];
        world.GetEntities(in _intentQuery, entities.AsSpan());

        foreach (var e in entities)
        {
            var body = world.Get<PhysicsBody>(e);
            if (body.Handle.IsNull) continue;

            var intent = world.Get<MoveIntent>(e);
            var velocity = physics.GetLinearVelocity(body.Handle);
            physics.SetLinearVelocity(body.Handle,
                new Vector3(intent.DirX * intent.Speed, velocity.Y, intent.DirZ * intent.Speed));
        }
    }

    /// <summary>消费一次性物理命令：冲量/传送经 <see cref="PhysicsWorld"/> 应用后，由命令缓冲在帧末统一移除组件。</summary>
    private void ApplyCommands(PhysicsWorld physics)
    {
        var world = Server.World;

        var count = world.CountEntities(in _impulseQuery);
        var entities = new Entity[count];
        world.GetEntities(in _impulseQuery, entities.AsSpan());
        foreach (var e in entities)
        {
            var body = world.Get<PhysicsBody>(e);
            if (!body.Handle.IsNull && body.Type == PhysicsBodyType.Dynamic)
                physics.ApplyLinearImpulse(body.Handle, world.Get<ApplyImpulse>(e).Impulse);
            Commands.Current.Remove<ApplyImpulse>(e);
        }

        count = world.CountEntities(in _teleportQuery);
        entities = new Entity[count];
        world.GetEntities(in _teleportQuery, entities.AsSpan());
        foreach (var e in entities)
        {
            var body = world.Get<PhysicsBody>(e);
            if (!body.Handle.IsNull)
            {
                var req = world.Get<TeleportRequest>(e);
                physics.Teleport(body.Handle, new Vector3((float)req.X, (float)req.Y, (float)req.Z));
            }
            Commands.Current.Remove<TeleportRequest>(e);
        }
    }

    /// <summary>同步刚体：为新增实体建体、为消失实体删体，并把初始位姿喂给 Bepu。</summary>
    private void SyncBodies(PhysicsWorld physics)
    {
        var world = Server.World;
        var count = world.CountEntities(in _bodyQuery);
        var entities = new Entity[count];
        world.GetEntities(in _bodyQuery, entities.AsSpan());

        _alive.Clear();
        foreach (var e in entities)
        {
            _alive.Add(e);
            var body = world.Get<PhysicsBody>(e);
            if (body.Handle.IsNull)
            {
                var position = world.TryGet<Position>(e, out var p) ? p : default;
                var rotation = world.TryGet<Rotation>(e, out var r) ? r : default;
                body.Handle = physics.CreateBody(
                    body.Shape,
                    new Vector3((float)position.X, (float)position.Y, (float)position.Z),
                    ToQuaternion(rotation),
                    body.Mass,
                    body.Type,
                    body.RotationLock);
                world.Set(e, body);
                Server.PhysicsRegistry.RegisterBody(e, body.Handle);
            }
        }

        RemoveTrackedBodies(physics);

        _trackedBodies.Clear();
        foreach (var e in entities)
            _trackedBodies[e] = world.Get<PhysicsBody>(e).Handle;
    }

    /// <summary>同步静态体：为新增实体建静态碰撞体、为消失实体删除。</summary>
    private void SyncStatics(PhysicsWorld physics)
    {
        var world = Server.World;
        var count = world.CountEntities(in _staticQuery);
        var entities = new Entity[count];
        world.GetEntities(in _staticQuery, entities.AsSpan());

        _alive.Clear();
        foreach (var e in entities)
        {
            _alive.Add(e);
            var stat = world.Get<PhysicsStatic>(e);
            if (stat.Handle.IsNull)
            {
                var position = world.TryGet<Position>(e, out var p) ? p : default;
                var rotation = world.TryGet<Rotation>(e, out var r) ? r : default;
                stat.Handle = physics.CreateStatic(
                    stat.Shape,
                    new Vector3((float)position.X, (float)position.Y, (float)position.Z),
                    ToQuaternion(rotation));
                world.Set(e, stat);
                Server.PhysicsRegistry.RegisterStatic(e, stat.Handle);
            }
        }

        _removeList.Clear();
        foreach (var e in _trackedStatics.Keys)
            if (!_alive.Contains(e)) _removeList.Add(e);
        foreach (var e in _removeList)
        {
            Server.PhysicsRegistry.UnregisterStatic(e);
            physics.RemoveStatic(_trackedStatics[e]);
            _trackedStatics.Remove(e);
        }

        _trackedStatics.Clear();
        foreach (var e in entities)
            _trackedStatics[e] = world.Get<PhysicsStatic>(e).Handle;
    }

    /// <summary>回收已消失（实体销毁或移除 PhysicsBody 组件）的刚体句柄。</summary>
    private void RemoveTrackedBodies(PhysicsWorld physics)
    {
        _removeList.Clear();
        foreach (var e in _trackedBodies.Keys)
            if (!_alive.Contains(e)) _removeList.Add(e);
        foreach (var e in _removeList)
        {
            Server.PhysicsRegistry.UnregisterBody(e);
            physics.RemoveBody(_trackedBodies[e]);
            _trackedBodies.Remove(e);
        }
    }

    /// <summary>把 Bepu 的位姿/线速度回写到同步组件（Position 为 double，做 float→double 加宽）。</summary>
    private void WriteBackBodies(PhysicsWorld physics)
    {
        var world = Server.World;
        var count = world.CountEntities(in _bodyQuery);
        var entities = new Entity[count];
        world.GetEntities(in _bodyQuery, entities.AsSpan());

        foreach (var e in entities)
        {
            var body = world.Get<PhysicsBody>(e);
            if (body.Handle.IsNull) continue;

            if (world.TryGet<Position>(e, out var position) && world.TryGet<Velocity>(e, out var velocity))
            {
                var pos = physics.GetPosition(body.Handle);
                var vel = physics.GetLinearVelocity(body.Handle);
                position.X = pos.X;
                position.Y = pos.Y;
                position.Z = pos.Z;
                velocity.X = vel.X;
                velocity.Y = vel.Y;
                velocity.Z = vel.Z;
                world.Set(e, position);
                world.Set(e, velocity);
            }

            if (world.TryGet<Rotation>(e, out var rotation))
            {
                var orient = physics.GetOrientation(body.Handle);
                rotation.X = orient.X;
                rotation.Y = orient.Y;
                rotation.Z = orient.Z;
                rotation.W = orient.W;
                world.Set(e, rotation);
            }
        }
    }

    /// <summary>把协议层的 <see cref="Rotation"/> 转成单位四元数；全零（未设置）视为恒等朝向。</summary>
    private static Quaternion ToQuaternion(in Rotation rotation)
    {
        return rotation.W == 0f && rotation.X == 0f && rotation.Y == 0f && rotation.Z == 0f
            ? Quaternion.Identity
            : new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
    }
}
