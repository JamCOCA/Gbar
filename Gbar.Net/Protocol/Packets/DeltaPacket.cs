using LiteNetLib;
using MemoryPack;
using Gbar.Net.Protocol;
using Gbar.Net.Protocol.Entities;

namespace Gbar.Net.Protocol.Packets;

/// <summary>
/// S→C 增量包（常态）。BaseTick = 该连接上次 ACK 的 tick，即本包相对基线；
/// 客户端校验 BaseTick == 本地 LastAppliedTick，不符则丢弃等 Keyframe。
/// Spawned 为新实体（完整状态）；Updated 为字段级增量（按 <see cref="EntityDelta.Mask"/> 只带变化字段，
/// 序列化由 <see cref="EntityDeltaArrayFormatter"/> 覆盖）。
/// 单包若超过 Sequenced 通道上限，由传输层自动分片（见 PacketCodec/FragmentAssembler），本协议无需感知。
/// </summary>
[Packet(PacketType.Delta, DeliveryMethod.Sequenced, PacketDirection.ServerToClient)]
[MemoryPackable]
public partial struct DeltaPacket
{
    public int BaseTick;
    public int CurrTick;
    public EntityState[] Spawned;
    [EntityDeltaArrayFormatter]
    public EntityDelta[] Updated;
    public int[] Despawned;
}
