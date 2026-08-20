using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

/// <summary>
/// 单个实体的同步状态，Keyframe/Delta 包体的基本元素。
/// 协议层不引用 ECS 组件，保持 Core 与传输解耦。
/// 坐标/速度为定点 long（1:100，见 GbarMotion.md），不用 float——
/// 星系级坐标超出 float 精度，且定点保证两端逐 bit 一致。
/// </summary>
[MemoryPackable]
public partial struct EntityState
{
    public int NetId;                   // 4 B
    public int SpaceNetId;              // 4 B  所属空间（ParentRef 指向实体的 NetId）；0 = 全局域
    public long PosX, PosY, PosZ;       // 24 B 定点位置（所在域坐标系）
    public long VelX, VelY, VelZ;       // 24 B 定点速度（着地实体由位置差分现算填充）
}
// 合计 56 B/实体（MemoryPack 头部按 ~2 B 摊销，按 58 B 估算）
