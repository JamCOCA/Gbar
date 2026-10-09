using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuUtilities;

namespace Gbar.Physics;

/// <summary>
/// 默认位姿积分回调：每个物理子步对刚体速度施加常重力（无阻尼）。
/// BepuPhysics v2 把"速度如何随时间变化"抽成 <see cref="IPoseIntegratorCallbacks"/> 接口，
/// 由 <see cref="PhysicsWorld"/> 构造时注入 <see cref="Simulation.Create"/>。
/// 若不实现本回调，Simulation 无法创建；引擎不内置"重力"这个游戏概念。
/// 单线程确定性：本回调不依赖线程局部状态，可安全内联推进。
/// </summary>
internal struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    /// <summary>世界重力加速度（米/秒²，如 (0,-10,0) 表示向下）。构造时固定，不可变。</summary>
    private readonly Vector3 _gravity;

    /// <summary>本步的重力速度变化量 Δv = gravity * dt，广播成宽向量供 <see cref="IntegrateVelocity"/> 复用。</summary>
    private Vector3Wide _gravityWideDt;

    public PoseIntegratorCallbacks(Vector3 gravity)
    {
        _gravity = gravity;
        _gravityWideDt = default;
    }

    /// <summary>角速度积分方式：Nonconserving 直接积分角速度、不守恒角动量（简单，多数游戏够用）。</summary>
    public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;

    /// <summary>无约束的刚体是否走子步细分。false = 单步积分，减少开销且保证确定性。</summary>
    public bool AllowSubstepsForUnconstrainedBodies => false;

    /// <summary>运动学物体（如固定平台/传送带）是否也调用本积分。false = 保持其手动设定的速度。</summary>
    public bool IntegrateVelocityForKinematics => false;

    /// <summary>Simulation 构造完成后调用一次。本实现无额外状态需要初始化。</summary>
    public void Initialize(Simulation simulation) { }

    /// <summary>本质就是求本步的重力速度变化量 Δv = g·dt，并广播成宽向量（各车道同值）。</summary>
    public void PrepareForIntegration(float dt)
    {
        _gravityWideDt = Vector3Wide.Broadcast(_gravity * dt);
    }

    /// <summary>核心积分：把上一步算好的 Δv 加到每个刚体的线速度上（velocity += g·dt）。只加重力，不做阻尼/角速度。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
    {
        velocity.Linear += _gravityWideDt;
    }
}
