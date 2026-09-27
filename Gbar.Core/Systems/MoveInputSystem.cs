using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Input;
using Gbar.Net.Protocol.Components;
using Gbar.Net.Protocol.Packets;

namespace Gbar.Core.Systems;

/// <summary>
/// 玩家移动输入系统（定步长，Order 50，跑在 MovementSystem 之前）：
/// 把 <see cref="MoveInput"/> 意图换算为 <see cref="Velocity"/>，并应用奔跑/蹲伏速度倍率。
/// 跳越属边沿触发且依赖地面状态，待重力/着地系统就绪后再接入。
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
    [All<PlayerTag, Velocity, MoveInput>]
    private static void ApplyMoveInput(ref Velocity velocity, in MoveInput input)
    {
        var speed = WalkSpeed;
        if ((input.Flags & MoveFlags.Running) != 0) speed *= RunMultiplier;
        if ((input.Flags & MoveFlags.Crouch) != 0) speed *= CrouchMultiplier;

        // 输入方向已由 NetServerSystem 归一化（模长 ≤ 1），直接缩放为速度
        velocity.X = input.X * speed;
        velocity.Z = input.Y * speed;
        // velocity.Y 留给跳越/重力处理（当前无垂直运动，保持 0）
    }
}
