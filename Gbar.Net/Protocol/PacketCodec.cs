using LiteNetLib;
using MemoryPack;

namespace Gbar.Net.Protocol;

/// <summary>
/// 封包编解码与分发。封包格式：[ PacketType: 1 byte ][ MemoryPack 序列化的包体 ]。
/// 本类不依赖 NetServer/NetClient，可独立单测。
/// </summary>
public static class PacketCodec
{
    /// <summary>把包体序列化并在前面拼上类型字节，得到可直接发送的完整封包。</summary>
    public static byte[] Encode<T>(PacketType type, in T body) where T : struct
    {
        var bodyBytes = MemoryPackSerializer.Serialize(body);
        var buffer = new byte[1 + bodyBytes.Length];
        buffer[0] = (byte)type;
        bodyBytes.CopyTo(buffer, 1);
        return buffer;
    }

    /// <summary>
    /// 按首字节识别包类型，反序列化包体后回调 <paramref name="handler"/> 对应方法。
    /// 返回 false 表示封包非法（空包、未知类型、或包体损坏），调用方应断开该连接，不做容错。
    /// </summary>
    public static bool TryDispatch(ReadOnlySpan<byte> data, IPacketHandler handler)
    {
        if (data.Length < 1) return false;
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
