using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;

namespace Gbar.Physics;

/// <summary>
/// 默认窄相碰撞回调：决定"哪些物体对允许产生接触"以及"接触的摩擦/恢复/弹簧参数"。
/// BepuPhysics v2 把碰撞规则抽成 <see cref="INarrowPhaseCallbacks"/> 接口，
/// 由 <see cref="PhysicsWorld"/> 构造时注入 <see cref="Simulation.Create"/>。
/// 若不实现本回调，Simulation 无法创建；引擎不内置"谁该跟谁碰撞"这个游戏概念。
/// 当前实现采用统一材质：所有动态体之间、动态体与静态体之间都可碰撞，参数全局一致。
/// </summary>
internal struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    /// <summary>接触弹簧：频率（Hz）与临界阻尼比。决定接触约束的软硬/穿透回正速度。</summary>
    public SpringSettings ContactSpringiness;

    /// <summary>最大恢复速度（米/秒）：接触分离时允许的最大反弹速度上限，避免高速弹飞。</summary>
    public float MaximumRecoveryVelocity;

    /// <summary>摩擦系数（0~∞）：影响切向滑动的阻力，1 为常见默认值。</summary>
    public float FrictionCoefficient;

    public NarrowPhaseCallbacks(SpringSettings contactSpringiness, float maximumRecoveryVelocity = 2f, float frictionCoefficient = 1f)
    {
        ContactSpringiness = contactSpringiness;
        MaximumRecoveryVelocity = maximumRecoveryVelocity;
        FrictionCoefficient = frictionCoefficient;
    }

    /// <summary>
    /// Simulation 构造完成后调用一次。若调用方用 default 构造（弹簧频率与阻尼比都是 0，属于非法值），
    /// 此处回退为默认接触弹簧：频率 30Hz、临界阻尼比 1（不反弹）。
    /// </summary>
    public void Initialize(Simulation simulation)
    {
        if (ContactSpringiness.AngularFrequency == 0 && ContactSpringiness.TwiceDampingRatio == 0)
            ContactSpringiness = new SpringSettings(30, 1);
    }

    /// <summary>
    /// 宽相阶段判断：是否允许这一对物体生成接触（可读/改 speculativeMargin 控制预测边距）。
    /// 当前规则：至少一方是动态体才允许，避免静态体之间的无意义接触。
    /// 碰撞过滤（如玩家/队友不碰撞、法术区域）应在此处按碰撞层扩展。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
    {
        return a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
    }

    /// <summary>
    /// 窄相阶段判断（带子碰撞体索引，用于复合形状）：是否允许这对子碰撞体生成接触。当前一律允许。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
    {
        return true;
    }

    /// <summary>
    /// 为凸接触流形配置材质参数（摩擦/恢复/弹簧）。此处写入统一材质，返回 true 表示接受该流形。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial.FrictionCoefficient = FrictionCoefficient;
        pairMaterial.MaximumRecoveryVelocity = MaximumRecoveryVelocity;
        pairMaterial.SpringSettings = ContactSpringiness;
        return true;
    }

    /// <summary>
    /// 非泛型重载：对直接持有凸流形的接触对同样返回 true，接受默认材质（由宽相/求解器沿用）。当前不做额外处理。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
    {
        return true;
    }

    /// <summary>Simulation 释放时调用。本实现无需要释放的资源。</summary>
    public void Dispose() { }
}
