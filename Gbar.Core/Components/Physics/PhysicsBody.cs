using Gbar.Physics;

namespace Gbar.Core.Components.Physics;

/// <summary>
/// 物理刚体组件（服务器本地，不同步）：把实体挂接到 Bepu 动态/运动学刚体。
/// 由 <see cref="Systems.PhysicsSystem"/> 惰性建体，并每 tick 把 Bepu 位姿回写到 Position/Rotation。
/// 移动由 <see cref="MoveIntent"/>（持续意图）或 <see cref="ApplyImpulse"/>/<see cref="TeleportRequest"/>（一次性命令）驱动，
/// 二者都由 PhysicsSystem 统一翻译执行，业务系统不直接触碰 <see cref="Handle"/>。
/// </summary>
public struct PhysicsBody
{
    /// <summary>刚体类型：Dynamic 受力/碰撞，Kinematic 手动设速度不受力。</summary>
    public PhysicsBodyType Type;

    /// <summary>质量（千克）。Kinematic 忽略此值（质量视为无穷大）。</summary>
    public float Mass;

    /// <summary>碰撞形状（盒/球/胶囊/网格）。</summary>
    public PhysicsShape Shape;

    /// <summary>Bepu 刚体句柄；<see cref="PhysicsBodyHandle.Null"/> 表示尚未建体，由 PhysicsSystem 惰性创建。</summary>
    public PhysicsBodyHandle Handle;

    public PhysicsBody(PhysicsBodyType type, in PhysicsShape shape, float mass = 1f)
    {
        Type = type;
        Shape = shape;
        Mass = mass;
        Handle = PhysicsBodyHandle.Null;
    }
}
