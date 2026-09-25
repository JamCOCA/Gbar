namespace Gbar.Net.Protocol.Entities;

/// <summary>
/// <see cref="EntityState"/> 可变字段的脏位掩码。字段级增量（<see cref="EntityDelta"/>）据此只序列化变化的字段。
/// NetId 是标识符，恒定发送，不在掩码内。
/// </summary>
[Flags]
public enum EntityFields : byte
{
    None = 0,
    PosX = 1 << 0,
    PosY = 1 << 1,
    PosZ = 1 << 2,
    VelX = 1 << 3,
    VelY = 1 << 4,
    VelZ = 1 << 5,
}

/// <summary>
/// 字段级增量：相对上次已 ACK 快照，只携带本 tick 变化的字段。
/// <see cref="Mask"/> 是 <see cref="EntityFields"/> 组合，序列化时仅写出被标记的字段。
/// 新实体仍走 DeltaPacket.Spawned（完整 <see cref="EntityState"/>），不走本结构。
/// 注意：本结构是 unmanaged，MemoryPack 对其数组默认按 C# 内存布局整块序列化（含 padding），
/// 因此必须由 formatter 手写序列化（见 Protocol 命名空间下的 EntityDeltaArrayFormatter），不能走自动 formatter。
/// </summary>
public struct EntityDelta
{
    public int NetId;
    public byte Mask;               // EntityFields 组合
    public double PosX, PosY, PosZ;
    public double VelX, VelY, VelZ;
}
