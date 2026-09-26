using LiteNetLib;
using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

/// <summary>C→S 版本协商包，协议版本不符服务器直接断开。</summary>
[Packet(PacketType.Hello, DeliveryMethod.ReliableOrdered, PacketDirection.ClientToServer)]
[MemoryPackable]
public partial struct HelloPacket
{
    public int ProtocolVersion;
}
