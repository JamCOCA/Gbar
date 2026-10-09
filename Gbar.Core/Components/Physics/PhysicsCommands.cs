using System.Numerics;

namespace Gbar.Core.Components.Physics;

/// <summary>
/// 一次性物理命令：只发生一次，由 <see cref="Systems.PhysicsSystem"/> 在固定步进内消费后经命令缓冲移除组件。
/// 业务系统只添加本组件表达意图，不直接触碰 <see cref="PhysicsBody.Handle"/> 或物理世界；
/// 与持续有效的 <see cref="MoveIntent"/> 相对，本文件内的组件都是"消费即失效"的命令。
/// </summary>

/// <summary>施加瞬时线冲量（N·s），用于击退、爆炸等。对 Kinematic 体无效（质量无穷大）。</summary>
public struct ApplyImpulse
{
    /// <summary>冲量向量（千克·米/秒）。</summary>
    public Vector3 Impulse;
}

/// <summary>瞬移请求：把刚体直接移到目标位置（传送/重生），绕开碰撞推挤。坐标精度与 <see cref="Gbar.Net.Protocol.Components.Position"/> 一致（double）。</summary>
public struct TeleportRequest
{
    public double X;
    public double Y;
    public double Z;
}
