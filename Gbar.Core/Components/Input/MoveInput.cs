using Gbar.Net.Protocol.Packets;

namespace Gbar.Core.Components.Input;

/// <summary>
/// 玩家移动输入状态（服务器本地，非同步组件）。由 NetServerSystem 校验后写入玩家实体，
/// 由 MoveInputSystem 每 tick 换算为 <see cref="Gbar.Core.Components.Physics.MoveIntent"/>。latest-wins：新输入直接覆盖旧值。
/// </summary>
public struct MoveInput
{
    public float X;
    public float Y;
    public MoveFlags Flags;
}
