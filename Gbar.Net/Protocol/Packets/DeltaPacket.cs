using LiteNetLib;
using MemoryPack;
using Gbar.Net.Protocol.Entities.State;

namespace Gbar.Net.Protocol.Packets;

/// <summary>
/// S→C 增量包（常态）。BaseTick = 该连接上次 ACK 的 tick，即本包相对基线；
/// 客户端校验 BaseTick == 本地 LastAppliedTick，不符则丢弃等 Keyframe。
/// Spawned 为新实体（完整组件列表）；Updated 为组件级增量（只带变化组件，
/// 序列化由 <see cref="Gbar.Net.Protocol.ComponentEntryArrayFormatter"/> 覆盖）。
/// 单包若超过 Sequenced 通道上限，由传输层自动分片（见 PacketCodec/FragmentAssembler），本协议无需感知。
/// </summary>
[Packet(PacketType.Delta, DeliveryMethod.Sequenced, PacketDirection.ServerToClient)]
[MemoryPackable]
public partial struct DeltaPacket
{
    public int BaseTick;
    public int CurrTick;
    public EntitySpawn[] Spawned;
    public EntityUpdate[] Updated;
    public int[] Despawned;
}
