using LiteNetLib;
using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

/// <summary>双向优雅断开包，携带原因便于对端日志与清理。</summary>
[Packet(PacketType.Bye, DeliveryMethod.ReliableOrdered, PacketDirection.Both)]
[MemoryPackable]
public partial struct ByePacket
{
    public string Reason;
}
