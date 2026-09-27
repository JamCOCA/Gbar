namespace Gbar.Net.Protocol.Tools;

/// <summary>
/// 包级压缩器接口：对完整封包字节做压缩/解压。
/// 压缩以"包（传输帧）"为单位，而不是以单个实体组件为单位——批量数据才有跨实体冗余可压缩。
/// 实现应为无状态、线程安全的纯函数（同一实例会被收发两端并发调用）。
/// </summary>
public interface IPacketCompressor
{
    byte[] Compress(byte[] data);
    byte[] Decompress(byte[] data);
}

/// <summary>
/// 不压缩的默认实现（占位钩子）。后续引入 LZ4/Zstd 时，
/// 替换 <see cref="PacketCodec.Compressor"/> 即可，收发两端无需其他改动。
/// </summary>
public sealed class NullPacketCompressor : IPacketCompressor
{
    public static readonly NullPacketCompressor Instance = new();

    public byte[] Compress(byte[] data) => data;
    public byte[] Decompress(byte[] data) => data;
}
