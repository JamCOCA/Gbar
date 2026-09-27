using System.Buffers;
using MemoryPack;

namespace Gbar.Net.Protocol.Entities;

/// <summary>
/// 单个可复制组件的编解码器：把 <see cref="ComponentEntry"/> 中该组件的有效字段写入/读出自
/// <see cref="MemoryPackWriter{TBufferWriter}"/> / <see cref="MemoryPackReader"/>，并提供按内容比对的 <see cref="Equals"/>。
/// 具体实现由源生成器按 <c>[SyncComponent]</c> 组件自动产出，序列化 formatter 只按 ID 查表转发。
/// </summary>
public interface IComponentCodec
{
    ReplicatedComponent Id { get; }

    void Write<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, in ComponentEntry entry)
        where TBufferWriter : IBufferWriter<byte>;

    void Read(ref MemoryPackReader reader, ref ComponentEntry entry);

    bool Equals(in ComponentEntry a, in ComponentEntry b);
}
