using Gbar.Net.Protocol.Packets;
using LiteNetLib;
using MemoryPack;

namespace Gbar.Net.Protocol;

/// <summary>
/// 封包编解码与分发。统一封包管线：
///   应用消息 = [ PacketType: 1 byte ][ MemoryPack 序列化的包体 ]
///   传输帧   = 应用消息，或 [ CompressFlag: 1 byte ][ PacketType ][ 压缩后的包体 ]（超过阈值才压缩）
///   分片包   = [ FragmentFlag: 1 byte ][ MessageId: 2 ][ FragIndex: 2 ][ FragCount: 2 ][ 帧片段 ]
/// 分片与压缩都发生在传输层，业务层（Keyframe/Delta 等）调用同一个 Send，无需感知。
/// 本类不依赖 NetServer/NetClient，可独立单测。
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

    /// <summary>把包体序列化并在前面拼上类型字节，得到应用消息（未压缩）。</summary>
    public static byte[] Encode<T>(PacketType type, in T body) where T : struct
    {
        var bodyBytes = MemoryPackSerializer.Serialize(body);
        var buffer = new byte[1 + bodyBytes.Length];
        buffer[0] = (byte)type;
        bodyBytes.CopyTo(buffer, 1);
        return buffer;
    }

    /// <summary>
    /// 应用消息 → 传输帧：超过 <see cref="MinCompressBytes"/> 且压缩有收益时加压缩标志，
    /// 否则原样返回（首字节即 PacketType）。
    /// </summary>
    public static byte[] EncodeFrame<T>(PacketType type, in T body) where T : struct
    {
        var message = Encode(type, body);
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
    /// 分发一个完整帧（已重组、可能带压缩标志）。返回 false 表示封包非法，调用方应断开该连接。
    /// </summary>
    public static bool TryDispatch(ReadOnlySpan<byte> frame, IPacketHandler handler)
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
            return DispatchPlain(message, handler);
        }

        return DispatchPlain(frame, handler);
    }

    private static bool DispatchPlain(ReadOnlySpan<byte> data, IPacketHandler handler)
    {
        var type = (PacketType)data[0];
        var body = data[1..];
        try
        {
            switch (type)
            {
                case PacketType.Hello:
                    handler.OnHello(MemoryPackSerializer.Deserialize<HelloPacket>(body));
                    return true;
                case PacketType.Welcome:
                    handler.OnWelcome(MemoryPackSerializer.Deserialize<WelcomePacket>(body));
                    return true;
                case PacketType.Galaxy:
                    handler.OnGalaxy(MemoryPackSerializer.Deserialize<GalaxyPacket>(body));
                    return true;
                case PacketType.Keyframe:
                    handler.OnKeyframe(MemoryPackSerializer.Deserialize<KeyframePacket>(body));
                    return true;
                case PacketType.Delta:
                    handler.OnDelta(MemoryPackSerializer.Deserialize<DeltaPacket>(body));
                    return true;
                case PacketType.Ack:
                    handler.OnAck(MemoryPackSerializer.Deserialize<AckPacket>(body));
                    return true;
                case PacketType.Bye:
                    handler.OnBye(MemoryPackSerializer.Deserialize<ByePacket>(body));
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            // 畸形包体（MemoryPack 反序列化失败）：视为非法封包，由调用方断开
            return false;
        }
    }

    /// <summary>
    /// 按包的语义选择 LiteNetLib 通道，内聚在此处使业务层无需感知：
    /// 握手/快照/断开必须可靠有序；Delta 用 Sequenced 丢旧留新避免队头阻塞（旧状态没有重传价值）；
    /// Ack 允许丢失，走裸不可靠通道。
    /// </summary>
    public static DeliveryMethod ChannelFor(PacketType type) => type switch
    {
        PacketType.Hello or PacketType.Welcome or PacketType.Galaxy or PacketType.Keyframe or PacketType.Bye
            => DeliveryMethod.ReliableOrdered,
        PacketType.Delta => DeliveryMethod.Sequenced,
        PacketType.Ack => DeliveryMethod.Unreliable,
        _ => DeliveryMethod.ReliableOrdered,
    };
}
