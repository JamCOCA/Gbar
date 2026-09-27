using System.Buffers;
using MemoryPack;
using Gbar.Net.Protocol.Entities;

namespace Gbar.Net.Protocol;

/// <summary>
/// 包体结构定义见 <see cref="Packets"/> 命名空间下的各文件；本文件仅保留包的序列化功能（formatter）。
/// </summary>

/// <summary>
/// <see cref="ComponentEntry"/> 数组的手写序列化：长度头 + 逐元素按 <see cref="ComponentEntry.Id"/> 写出，
/// 未激活的组件字段不占字节。ComponentEntry 是"标签联合"结构体，默认会被 MemoryPack 按内存布局整块序列化
/// （含空槽 padding），故用本 formatter 覆盖。经 <see cref="ComponentEntryArrayFormatterAttribute"/> 关联到
/// EntitySpawn.Components / EntityUpdate.Components 字段。
/// </summary>
public sealed class ComponentEntryArrayFormatter : MemoryPackFormatter<ComponentEntry[]>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref ComponentEntry[]? value)
    {
        if (value is null)
        {
            writer.WriteNullCollectionHeader();
            return;
        }
        writer.WriteCollectionHeader(value.Length);
        foreach (var e in value)
            WriteEntry(ref writer, e);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref ComponentEntry[]? value)
    {
        reader.TryReadCollectionHeader(out var count);
        value = new ComponentEntry[count];
        for (var i = 0; i < count; i++)
            value[i] = ReadEntry(ref reader);
    }

    internal static void WriteEntry<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, in ComponentEntry e)
        where TBufferWriter : IBufferWriter<byte>
    {
        writer.WriteUnmanaged((byte)e.Id);
        if (!ComponentCodecRegistry.Codecs.TryGetValue(e.Id, out var codec))
            throw new InvalidDataException($"未知组件 ID {e.Id}");
        codec.Write(ref writer, in e);
    }

    internal static ComponentEntry ReadEntry(ref MemoryPackReader reader)
    {
        var id = (ReplicatedComponent)reader.ReadUnmanaged<byte>();
        if (!ComponentCodecRegistry.Codecs.TryGetValue(id, out var codec))
            throw new InvalidDataException($"未知组件 ID {id}");
        var e = new ComponentEntry { Id = id };
        codec.Read(ref reader, ref e);
        return e;
    }
}

/// <summary>
/// 把 <see cref="ComponentEntryArrayFormatter"/> 关联到 EntitySpawn.Components / EntityUpdate.Components 字段。
/// MemoryPack 源生成器看到字段上的该 attribute，会用 <see cref="GetFormatter"/> 返回的 formatter 序列化该字段，
/// 并由生成器自动注册——属 CA2255 允许的源生成器场景。
/// </summary>
public sealed class ComponentEntryArrayFormatterAttribute : MemoryPackCustomFormatterAttribute<ComponentEntryArrayFormatter, ComponentEntry[]>
{
    public override ComponentEntryArrayFormatter GetFormatter() => new();
}
