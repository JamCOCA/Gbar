using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Input;
using Gbar.Core.Components.Physics;
using Gbar.Net.Protocol.Packets;

namespace Gbar.Core.Systems;

/// <summary>
/// 玩家移动输入系统（定步长，Order 50，跑在 PhysicsSystem 之前）：
/// 把 <see cref="MoveInput"/> 意图换算为 <see cref="MoveIntent"/>（方向 + 目标速度），并应用奔跑/蹲伏速度倍率。
/// 本系统不碰物理：Velocity/Position 由 PhysicsSystem 统一执行与回写。
/// 跳跃属边沿触发且依赖地面状态，待着地系统就绪后以后续命令组件接入。
/// </summary>
[SystemOrder(50)]
public sealed partial class MoveInputSystem : FixedSystem
{
    /// <summary>基准行走速度（米/秒）。</summary>
    private const float WalkSpeed = 5f;

    /// <summary>奔跑速度倍率。</summary>
    private const float RunMultiplier = 1.8f;

    /// <summary>蹲伏速度倍率。</summary>
    private const float CrouchMultiplier = 0.4f;

    public MoveInputSystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        ApplyMoveInputQuery(World);
    }

    [Query(Parallel = true)]
    [All<PlayerTag, MoveInput, MoveIntent>]
    private static void ApplyMoveInput(ref MoveIntent intent, in MoveInput input)
    {
        var speed = WalkSpeed;
        if ((input.Flags & MoveFlags.Running) != 0) speed *= RunMultiplier;
        if ((input.Flags & MoveFlags.Crouch) != 0) speed *= CrouchMultiplier;

        // 输入方向已由 NetServerSystem 归一化（模长 ≤ 1），原样写入意图，由 PhysicsSystem 缩放为速度
        intent.DirX = input.X;
        intent.DirZ = input.Y;
        intent.Speed = speed;
    }
}
