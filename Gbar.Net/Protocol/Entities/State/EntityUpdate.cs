using MemoryPack;

namespace Gbar.Net.Protocol.Entities.State;

/// <summary>
/// 已存在实体的组件级增量（Delta.Updated 元素）：只携带本 tick 变化的组件。
/// 组件列表里出现的组件 = 相对该连接上次 ACK 基线已变化，未出现 = 未变。
/// 组件集合在实体生命周期内保持稳定（出生时由 EntityType 决定装配），故无需"组件移除"标记。
/// </summary>
[MemoryPackable]
public partial struct EntityUpdate
{
    public int NetId;
    [ComponentEntryArrayFormatter]
    public ComponentEntry[] Components;
}
