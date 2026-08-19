namespace Gbar.Net.Protocol;

/// <summary>
/// 分片重组器：把同一消息的多个分片按序累积，收齐后吐出完整帧。
/// 每个连接持有一个实例（接收路径单线程访问，无需加锁）。
/// 仅用于 Sequenced 等不可分片通道的应用层分包；ReliableOrdered 由 LiteNetLib 底层分片，
/// 到本层时已是完整包，不会出现分片标记。
/// </summary>
public sealed class FragmentAssembler
{
    private int _expectedId = -1;   // 正在重组的消息 MessageId（-1 = 空闲）
    private int _expectedCount;     // 期望分片总数
    private int _received;          // 已收到的分片数
    private readonly List<byte[]> _parts = new();

    /// <summary>
    /// 接收一个底层包：分片包则累积（未收齐返回 null）；非分片包原样返回。
    /// 收齐后返回重组出的完整帧（可能带压缩标志，交由 <see cref="PacketCodec.TryDispatch"/> 处理）。
    /// 丢片/乱序时静默丢弃当前未完成消息——对端用 Keyframe/下一完整消息兜底。
    /// </summary>
    public byte[]? Accept(byte[] data)
    {
        if (data.Length == 0) return data;
        if (data[0] != PacketCodec.FragmentFlag) return data;

        if (data.Length < PacketCodec.FragmentHeaderSize) return null;

        var messageId = (ushort)(data[1] | (data[2] << 8));
        var fragIndex = (ushort)(data[3] | (data[4] << 8));
        var fragCount = (ushort)(data[5] | (data[6] << 8));
        var payload = new byte[data.Length - PacketCodec.FragmentHeaderSize];
        Array.Copy(data, PacketCodec.FragmentHeaderSize, payload, 0, payload.Length);

        if (fragIndex == 0)
        {
            // 新消息开始；上一消息若未收齐即视为丢片，丢弃
            _expectedId = messageId;
            _expectedCount = fragCount;
            _received = 0;
            _parts.Clear();
        }

        // 顺序校验：必须是当前消息，且 FragIndex 严格递增（有序通道保证不乱序）
        if (messageId != _expectedId || fragIndex != _received)
        {
            _expectedId = -1;
            _parts.Clear();
            return null;
        }

        _parts.Add(payload);
        _received++;

        if (_received < _expectedCount) return null;

        var total = 0;
        foreach (var p in _parts) total += p.Length;
        var frame = new byte[total];
        var offset = 0;
        foreach (var p in _parts)
        {
            p.CopyTo(frame, offset);
            offset += p.Length;
        }

        _expectedId = -1;
        _parts.Clear();
        return frame;
    }
}
