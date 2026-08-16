using System.Text.Json;

namespace Gbar.Core.Persistence;

/// <summary>单个实体的持久化快照：一组组件数据。</summary>
public sealed class EntitySnapshot
{
    public List<ComponentSnapshot> Components { get; set; } = new();
}

/// <summary>单个组件的持久化快照：类型名 + JSON 数据。</summary>
public sealed class ComponentSnapshot
{
    /// <summary>组件类型的 AssemblyQualifiedName，加载时据此还原类型。</summary>
    public string Type { get; set; } = string.Empty;

    public JsonElement Data { get; set; }
}

/// <summary>整个世界的持久化快照。</summary>
public sealed class WorldSnapshot
{
    public int Version { get; set; } = 1;

    public List<EntitySnapshot> Entities { get; set; } = new();
}
