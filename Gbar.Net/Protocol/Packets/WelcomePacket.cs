using LiteNetLib;
using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

/// <summary>S→C 身份分配包；PlayerNetId 为服务器为该连接创建的玩家本体实体。ServerTick 用于两端 tick 对齐。</summary>
[Packet(PacketType.Welcome, DeliveryMethod.ReliableOrdered, PacketDirection.ServerToClient)]
[MemoryPackable]
public partial struct WelcomePacket
{
    public int ClientId;
    public int TickRate;
    public int ServerTick;
    public int PlayerNetId;
}
