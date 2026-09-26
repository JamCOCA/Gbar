using LiteNetLib;

namespace Gbar.Net.Protocol;

/// <summary>包的传输方向。</summary>
public enum PacketDirection : byte
{
    ClientToServer,
    ServerToClient,
    Both,
}

/// <summary>
/// 包的协议元数据，写在结构体上作为唯一事实源：
/// 类型字节、LiteNetLib 通道、传输方向。分发、通道选择、方向校验都由此推导，无需再改任何 switch。
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class PacketAttribute : Attribute
{
    public PacketType Type { get; }
    public DeliveryMethod Channel { get; }
    public PacketDirection Direction { get; }

    public PacketAttribute(PacketType type, DeliveryMethod channel, PacketDirection direction)
    {
        Type = type;
        Channel = channel;
        Direction = direction;
    }
}
