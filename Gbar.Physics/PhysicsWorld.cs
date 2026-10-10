using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Gbar.Physics;

/// <summary>
/// BepuPhysics v2 封装：持有 <see cref="Simulation"/>/<see cref="BufferPool"/>/<see cref="ThreadDispatcher"/> 生命周期，
/// 提供动态/静态刚体的创建、位姿读写、移除与单步推进。
/// 线程模型：workerThreads = 0 时单线程内联推进（默认，保证 run-to-run 确定性），>0 时用 Bepu 自带线程池。
/// 对外的句柄类型（<see cref="PhysicsBodyHandle"/>/<see cref="PhysicsStaticHandle"/>）是不透明封装，
/// 使调用方（Gbar.Core）无需直接依赖 BepuPhysics 类型。
/// </summary>
public sealed class PhysicsWorld : IDisposable
{
    private readonly BufferPool _pool;
    private readonly ThreadDispatcher? _dispatcher;
    private readonly ShapeManager _shapes;
    private bool _disposed;

    /// <summary>底层物理模拟，调用方不直接持有，统一经本封装访问。</summary>
    public Simulation Simulation { get; }

    public PhysicsWorld(Vector3 gravity, int workerThreads = 0)
    {
        _pool = new BufferPool();
        if (workerThreads > 0)
            _dispatcher = new ThreadDispatcher(workerThreads);

        Simulation = Simulation.Create(
            _pool,
            new NarrowPhaseCallbacks(new SpringSettings(30, 1)),
            new PoseIntegratorCallbacks(gravity),
            new SolveDescription(8, 1));

        _shapes = new ShapeManager(_pool, Simulation);
    }

    /// <summary>按固定步长推进一次模拟（dt 必须恒定，保证确定性）。</summary>
    public void Step(float dt) => Simulation.Timestep(dt, _dispatcher);

    /// <summary>创建动态/运动学刚体（盒/球/胶囊），返回句柄。Static 请用 <see cref="CreateStatic"/>。</summary>
    /// <param name="rotationLock">仅对 Dynamic 有效：被锁轴的逆惯量对角分量置零（转动惯量视为无穷大），碰撞/冲量不会使其绕该轴旋转。</param>
    public PhysicsBodyHandle CreateBody(in PhysicsShape shape, in Vector3 position, in Quaternion orientation, float mass, PhysicsBodyType type, RotationLock rotationLock = RotationLock.None)
    {
        var pose = new RigidPose(position, orientation);

        TypedIndex shapeIndex = _shapes.Register(in shape);
        // 只有动态体需要惯量；运动学体质量视为无穷大，惯量字段被 Bepu 忽略。
        // Mesh 不提供解析惯量（且定位是静态几何），动态 Mesh 不受支持。
        BodyInertia inertia = type == PhysicsBodyType.Dynamic ? ShapeManager.ComputeInertia(in shape, mass) : default;
        if (rotationLock.HasFlag(RotationLock.X)) inertia.InverseInertiaTensor.XX = 0f;   // 零逆惯量 = 无穷大转动惯量 → 该轴不旋转
        if (rotationLock.HasFlag(RotationLock.Y)) inertia.InverseInertiaTensor.YY = 0f;
        if (rotationLock.HasFlag(RotationLock.Z)) inertia.InverseInertiaTensor.ZZ = 0f;

        var collidable = new CollidableDescription(shapeIndex, 0.1f);
        var activity = new BodyActivityDescription(0.01f);

        BodyDescription description = type switch
        {
            PhysicsBodyType.Dynamic => BodyDescription.CreateDynamic(pose, inertia, collidable, activity),
            PhysicsBodyType.Kinematic => BodyDescription.CreateKinematic(pose, collidable, activity),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "静态体请使用 CreateStatic。"),
        };
        var handle = Simulation.Bodies.Add(description);
        return new PhysicsBodyHandle(handle.Value);
    }

    /// <summary>创建静态碰撞体（盒/球/胶囊/网格），返回句柄。静态体不参与动力学，仅作碰撞阻挡。Mesh 主要面向静态场景几何。</summary>
    public PhysicsStaticHandle CreateStatic(in PhysicsShape shape, in Vector3 position, in Quaternion orientation)
    {
        var description = new StaticDescription(new RigidPose(position, orientation), _shapes.Register(shape));
        var handle = Simulation.Statics.Add(description);
        return new PhysicsStaticHandle(handle.Value);
    }

    /// <summary>移除刚体（连同其碰撞体）。</summary>
    public void RemoveBody(PhysicsBodyHandle handle) => Simulation.Bodies.Remove(new BodyHandle { Value = handle.Value });

    /// <summary>移除静态碰撞体。</summary>
    public void RemoveStatic(PhysicsStaticHandle handle) => Simulation.Statics.Remove(new StaticHandle { Value = handle.Value });

    /// <summary>读取刚体位置（世界坐标，米）。</summary>
    public Vector3 GetPosition(PhysicsBodyHandle handle) => GetBody(handle).Pose.Position;

    /// <summary>读取刚体朝向（单位四元数）。</summary>
    public Quaternion GetOrientation(PhysicsBodyHandle handle) => GetBody(handle).Pose.Orientation;

    /// <summary>读取刚体线速度（米/秒）。</summary>
    public Vector3 GetLinearVelocity(PhysicsBodyHandle handle) => GetBody(handle).Velocity.Linear;

    /// <summary>设置刚体线速度并唤醒（常用于运动学体或玩家输入落速度）。</summary>
    public void SetLinearVelocity(PhysicsBodyHandle handle, in Vector3 velocity)
    {
        GetBody(handle).Velocity.Linear = velocity;
        Simulation.Awakener.AwakenBody(new BodyHandle { Value = handle.Value });
    }

    /// <summary>对刚体施加瞬时线冲量（用于爆炸、击退等）。Bepu 的 ApplyLinearImpulse 不自动唤醒休眠体，需手动唤醒，否则冲量延迟到下次唤醒才生效。</summary>
    public void ApplyLinearImpulse(PhysicsBodyHandle handle, in Vector3 impulse)
    {
        GetBody(handle).ApplyLinearImpulse(in impulse);
        Simulation.Awakener.AwakenBody(new BodyHandle { Value = handle.Value });
    }

    /// <summary>瞬移刚体到指定位置（传送/重生）：直接改写位姿并唤醒，不经过碰撞推挤。</summary>
    public void Teleport(PhysicsBodyHandle handle, in Vector3 position)
    {
        GetBody(handle).Pose.Position = position;
        Simulation.Awakener.AwakenBody(new BodyHandle { Value = handle.Value });
    }

    private BodyReference GetBody(PhysicsBodyHandle handle) => Simulation.Bodies.GetBodyReference(new BodyHandle { Value = handle.Value });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Simulation.Dispose();
        _dispatcher?.Dispose();
        _pool.Clear();
    }
}
