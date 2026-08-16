using Arch.Core;
using System.Text.Json;
using Gbar.Core.Logging;

namespace Gbar.Core.Persistence;

/// <summary>
/// JSON 文件持久化实现：遍历 World 全部实体，把通过过滤规则的组件序列化为 JSON。
/// 保存规则可通过 <see cref="EntityFilter"/>/<see cref="ComponentFilter"/> 自定义（后续可换源码生成器标注）。
/// </summary>
public sealed class JsonPersistenceProvider : IPersistenceProvider
{
    private static readonly QueryDescription _allEntities = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    /// <summary>实体级过滤：返回 true 才保存该实体。默认保存全部。</summary>
    public Func<Entity, bool>? EntityFilter { get; set; }

    /// <summary>组件级过滤：返回 true 才保存该组件。默认保存全部。</summary>
    public Func<Type, bool>? ComponentFilter { get; set; }

    public JsonPersistenceProvider(string path)
    {
        _path = path;
    }

    public Task SaveAsync(World world, CancellationToken ct = default)
    {
        var count = world.CountEntities(in _allEntities);
        var entities = new Entity[count];
        world.GetEntities(in _allEntities, entities.AsSpan());

        var snapshot = new WorldSnapshot();
        foreach (var entity in entities)
        {
            ct.ThrowIfCancellationRequested();
            if (EntityFilter is not null && !EntityFilter(entity)) continue;

            var entitySnapshot = new EntitySnapshot();
            foreach (var component in world.GetAllComponents(entity))
            {
                if (component is null) continue;
                var type = component.GetType();
                if (ComponentFilter is not null && !ComponentFilter(type)) continue;

                entitySnapshot.Components.Add(new ComponentSnapshot
                {
                    Type = type.AssemblyQualifiedName ?? type.FullName!,
                    Data = JsonSerializer.SerializeToElement(component, type, _jsonOptions),
                });
            }

            if (entitySnapshot.Components.Count > 0)
                snapshot.Entities.Add(entitySnapshot);
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        return File.WriteAllTextAsync(_path, json, ct);
    }

    public async Task LoadAsync(World world, CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return;

        var json = await File.ReadAllTextAsync(_path, ct);
        var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(json, _jsonOptions);
        if (snapshot is null) return;

        foreach (var entitySnapshot in snapshot.Entities)
        {
            ct.ThrowIfCancellationRequested();
            var entity = world.Create();
            foreach (var componentSnapshot in entitySnapshot.Components)
            {
                var type = Type.GetType(componentSnapshot.Type);
                if (type is null)
                {
                    Log.Warn($"持久化加载：无法解析组件类型 {componentSnapshot.Type}，已跳过");
                    continue;
                }

                object? component;
                try
                {
                    component = componentSnapshot.Data.Deserialize(type, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    Log.Warn($"持久化加载：组件 {type.FullName} 反序列化失败，已跳过：{ex.Message}");
                    continue;
                }

                if (component is not null)
                    world.Add(entity, component);
            }
        }
    }
}
