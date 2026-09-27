using Gbar.Net.Protocol.Packets;

namespace Gbar.Core.Components.Input;

/// <summary>
/// 玩家移动意图（服务器本地状态，非同步组件）。由 NetServerSystem 校验后写入玩家实体，
/// 由玩家移动系统消费并换算为 Velocity。latest-wins：新输入直接覆盖旧值。
/// </summary>
public struct MoveInput
{
    public float X;
    public float Y;
    public MoveFlags Flags;
}
