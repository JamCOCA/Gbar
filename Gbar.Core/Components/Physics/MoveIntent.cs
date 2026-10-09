namespace Gbar.Core.Components.Physics;

/// <summary>
/// 持续移动意图组件（服务器本地，非同步）：期望的水平移动方向与目标速度，
/// 由 <see cref="Systems.PhysicsSystem"/> 每 tick 翻译成刚体线速度（仅对 Kinematic 体生效，Dynamic 体由力/冲量驱动）。
/// 语义是"每 tick 都有效"的状态型意图，直到组件被移除；与之相对的一次性命令见 <see cref="ApplyImpulse"/>/<see cref="TeleportRequest"/>。
/// 业务系统只写本组件，不直接触碰 <see cref="PhysicsBody.Handle"/> 或物理世界。
/// </summary>
public struct MoveIntent
{
    /// <summary>期望移动方向的水平分量 X（输入侧已归一化，模长 ≤ 1）。</summary>
    public float DirX;

    /// <summary>期望移动方向的水平分量 Z（输入侧已归一化，模长 ≤ 1）。</summary>
    public float DirZ;

    /// <summary>目标水平速度（米/秒），由输入系统按行走/奔跑/蹲伏倍率换算。</summary>
    public float Speed;
}
