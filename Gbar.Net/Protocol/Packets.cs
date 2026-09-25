using System.Buffers;
using MemoryPack;
using Gbar.Net.Protocol.Entities;
using Gbar.Net.Protocol.Packets;

namespace Gbar.Net.Protocol;

/// <summary>
/// 包体结构定义见 <see cref="Packets"/> 命名空间下的各文件；本文件仅保留包的序列化功能（formatter）。
/// </summary>

/// <summary>
/// <see cref="EntityDelta"/> 数组的手写序列化：长度头 + 逐元素按 <see cref="EntityDelta.Mask"/> 写出，
/// 未变化字段不占字节。EntityDelta 是 unmanaged struct，默认会被 MemoryPack 按内存布局整块序列化（含 padding），
/// 故用本 formatter 覆盖。经 <see cref="EntityDeltaArrayFormatterAttribute"/> 关联到 DeltaPacket.Updated 字段。
/// </summary>
public sealed class EntityDeltaArrayFormatter : MemoryPackFormatter<EntityDelta[]>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref EntityDelta[]? value)
    {
        if (value is null)
        {
            writer.WriteNullCollectionHeader();
            return;
        }
        writer.WriteCollectionHeader(value.Length);
        foreach (var d in value)
            WriteEntityDelta(ref writer, d);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref EntityDelta[]? value)
    {
        reader.TryReadCollectionHeader(out var count);
        value = new EntityDelta[count];
        for (var i = 0; i < count; i++)
        {
            ref var d = ref value[i];
            d.NetId = reader.ReadUnmanaged<int>();
            d.Mask = reader.ReadUnmanaged<byte>();
            if ((d.Mask & (byte)EntityFields.PosX) != 0) d.PosX = reader.ReadUnmanaged<double>();
            if ((d.Mask & (byte)EntityFields.PosY) != 0) d.PosY = reader.ReadUnmanaged<double>();
            if ((d.Mask & (byte)EntityFields.PosZ) != 0) d.PosZ = reader.ReadUnmanaged<double>();
            if ((d.Mask & (byte)EntityFields.VelX) != 0) d.VelX = reader.ReadUnmanaged<double>();
            if ((d.Mask & (byte)EntityFields.VelY) != 0) d.VelY = reader.ReadUnmanaged<double>();
            if ((d.Mask & (byte)EntityFields.VelZ) != 0) d.VelZ = reader.ReadUnmanaged<double>();
        }
    }

    private static void WriteEntityDelta<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, in EntityDelta d)
        where TBufferWriter : IBufferWriter<byte>
    {
        writer.WriteUnmanaged(d.NetId);
        writer.WriteUnmanaged(d.Mask);
        if ((d.Mask & (byte)EntityFields.PosX) != 0) writer.WriteUnmanaged(d.PosX);
        if ((d.Mask & (byte)EntityFields.PosY) != 0) writer.WriteUnmanaged(d.PosY);
        if ((d.Mask & (byte)EntityFields.PosZ) != 0) writer.WriteUnmanaged(d.PosZ);
        if ((d.Mask & (byte)EntityFields.VelX) != 0) writer.WriteUnmanaged(d.VelX);
        if ((d.Mask & (byte)EntityFields.VelY) != 0) writer.WriteUnmanaged(d.VelY);
        if ((d.Mask & (byte)EntityFields.VelZ) != 0) writer.WriteUnmanaged(d.VelZ);
    }
}

/// <summary>
/// 把 <see cref="EntityDeltaArrayFormatter"/> 关联到 <see cref="DeltaPacket.Updated"/> 字段。
/// MemoryPack 源生成器看到字段上的该 attribute，会用 <see cref="GetFormatter"/> 返回的 formatter
/// 序列化该字段（而非默认的 unmanaged 数组），并由生成器自动注册——属 CA2255 允许的源生成器场景。
/// </summary>
public sealed class EntityDeltaArrayFormatterAttribute : MemoryPackCustomFormatterAttribute<EntityDeltaArrayFormatter, EntityDelta[]>
{
    public override EntityDeltaArrayFormatter GetFormatter() => new();
}
