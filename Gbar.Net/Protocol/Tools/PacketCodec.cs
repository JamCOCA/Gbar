using MemoryPack;

namespace Gbar.Net.Protocol.Tools;

/// <summary>
/// 封包编解码（发送侧）+ 压缩。统一封包管线：
///   应用消息 = [ PacketType: 1 byte ][ MemoryPack 序列化的包体 ]
///   传输帧   = 应用消息，或 [ CompressFlag: 1 byte ][ 压缩后的应用消息 ]
///   分片包   = [ FragmentFlag: 1 byte ][ MessageId: 2 ][ FragIndex: 2 ][ FragCount: 2 ][ 帧片段 ]
/// 接收侧分发由 <see cref="PacketDispatcher"/> 负责（无 switch），本类只处理解压与转发。
/// </summary>
public static class PacketCodec
{
    /// <summary>压缩帧标志（占帧首字节）。</summary>
    public const byte CompressFlag = 0xFE;

    /// <summary>分片包标志（占包首字节）。</summary>
    public const byte FragmentFlag = 0xFF;

    /// <summary>分片头长度：[flag:1][MessageId:2][FragIndex:2][FragCount:2]。</summary>
    public const int FragmentHeaderSize = 7;

    /// <summary>包级压缩器，默认不压缩；引入 LZ4/Zstd 后替换即可。</summary>
    public static IPacketCompressor Compressor { get; set; } = NullPacketCompressor.Instance;

    /// <summary>小于该字节数的帧不压缩（小包压缩反而膨胀）。</summary>
    public static int MinCompressBytes { get; set; } = 256;

    /// <summary>把包体序列化并在前面拼上类型字节，得到应用消息（未压缩）。类型字节来自 [Packet] 特性。</summary>
    public static byte[] Encode<T>(in T body) where T : struct
    {
        var bodyBytes = MemoryPackSerializer.Serialize(body);
        var buffer = new byte[1 + bodyBytes.Length];
        buffer[0] = PacketMeta<T>.Type;
        bodyBytes.CopyTo(buffer, 1);
        return buffer;
    }

    /// <summary>
    /// 应用消息 → 传输帧：超过 <see cref="MinCompressBytes"/> 且压缩有收益时加压缩标志，
    /// 否则原样返回（首字节即 PacketType）。
    /// </summary>
    public static byte[] EncodeFrame<T>(in T body) where T : struct
    {
        var message = Encode(body);
        if (message.Length < MinCompressBytes) return message;

        var compressed = Compressor.Compress(message);
        if (compressed.Length >= message.Length) return message; // 无收益，直接用原文

        var frame = new byte[1 + compressed.Length];
        frame[0] = CompressFlag;
        compressed.CopyTo(frame, 1);
        return frame;
    }

    /// <summary>
    /// 把传输帧拆成 ≤ <paramref name="maxSinglePacketSize"/> 的分片序列。
    /// 帧本身不超限时返回单元素（原帧，不带分片头）。
    /// </summary>
    public static List<byte[]> SplitFrame(byte[] frame, int maxSinglePacketSize, ushort messageId)
    {
        if (frame.Length <= maxSinglePacketSize)
            return new List<byte[]> { frame };

        var maxPayload = maxSinglePacketSize - FragmentHeaderSize;
        if (maxPayload <= 0) throw new InvalidOperationException("单包上限过小，无法分片。");

        var fragCount = (frame.Length + maxPayload - 1) / maxPayload;
        if (fragCount > ushort.MaxValue) throw new InvalidOperationException("分片数超出上限。");

        var result = new List<byte[]>(fragCount);
        for (var i = 0; i < fragCount; i++)
        {
            var offset = i * maxPayload;
            var len = Math.Min(maxPayload, frame.Length - offset);
            var frag = new byte[FragmentHeaderSize + len];
            frag[0] = FragmentFlag;
            frag[1] = (byte)(messageId & 0xFF);
            frag[2] = (byte)((messageId >> 8) & 0xFF);
            frag[3] = (byte)(i & 0xFF);
            frag[4] = (byte)((i >> 8) & 0xFF);
            frag[5] = (byte)(fragCount & 0xFF);
            frag[6] = (byte)((fragCount >> 8) & 0xFF);
            Array.Copy(frame, offset, frag, FragmentHeaderSize, len);
            result.Add(frag);
        }
        return result;
    }

    /// <summary>
    /// 分发一个完整帧（已重组、可能带压缩标志）：解压后交给 <paramref name="dispatcher"/>。
    /// 返回 false 表示封包非法或未知类型，调用方应断开该连接。
    /// </summary>
    public static bool TryDispatch(ReadOnlySpan<byte> frame, int clientId, PacketDispatcher dispatcher)
    {
        if (frame.Length < 1) return false;

        if (frame[0] == CompressFlag)
        {
            if (frame.Length < 2) return false;
            byte[] message;
            try
            {
                message = Compressor.Decompress(frame[1..].ToArray());
            }
            catch
            {
                return false; // 解压失败视为非法封包
            }
            return dispatcher.Dispatch(clientId, message);
        }

        return dispatcher.Dispatch(clientId, frame);
    }
}
