using MemoryPack;

namespace Gbar.Net.Protocol.Entities.State;

/// <summary>
/// 新实体的完整同步状态（Keyframe 元素 / Delta.Spawned 元素）。
/// 线格式：NetId + Type + 组件列表；组件列表按 <see cref="ReplicatedComponent"/> 标签逐个写出，
/// 实体只携带它实际持有的组件（投射物只有 Position/Velocity，战斗单位另有 Health/Cpc）。
/// <see cref="Type"/> 在实体创建时确定、终生不变，只随本结构全量下发，不参与组件级增量。
/// </summary>
[MemoryPackable]
public partial struct EntitySpawn
{
    public int NetId;
    public EntityType Type;
    [ComponentEntryArrayFormatter]
    public ComponentEntry[] Components;
}
