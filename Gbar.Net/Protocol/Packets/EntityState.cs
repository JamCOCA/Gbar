using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

/// <summary>
/// 单个实体的同步状态，Keyframe/Delta 包体的基本元素。
/// 协议层不引用 ECS 组件，保持 Core 与传输解耦。
/// 坐标为双精度浮点（米），服务器权威，客户端做插值/预测即可，无需逐 bit 一致。
/// </summary>
[MemoryPackable]
public partial struct EntityState
{
    public int NetId;                   // 4 B
    public double PosX, PosY, PosZ;     // 24 B 位置（米）
    public double VelX, VelY, VelZ;     // 24 B 速度（米/秒）
}
