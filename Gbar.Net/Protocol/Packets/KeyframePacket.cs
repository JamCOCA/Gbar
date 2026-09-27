using LiteNetLib;
using MemoryPack;
using Gbar.Net.Protocol.Entities.State;

namespace Gbar.Net.Protocol.Packets;

/// <summary>S→C 全量快照包：新连接对齐 / 周期纠偏 / 长时间未 ACK 回退重建三种情况发送。</summary>
[Packet(PacketType.Keyframe, DeliveryMethod.ReliableOrdered, PacketDirection.ServerToClient)]
[MemoryPackable]
public partial struct KeyframePacket
{
    public int Tick;
    public EntitySpawn[] Entities;
}
