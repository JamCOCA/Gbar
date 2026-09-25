namespace Gbar.Net.Protocol;

/// <summary>
/// 应用层协议包类型，占封包首字节（1 byte）。
/// 新增包类型时需在 <see cref="PacketCodec.TryDispatch"/> 的 switch 与
/// <see cref="IPacketHandler"/> 中同步补充分支，否则该包会被静默丢弃（TryDispatch 返回 false）。
/// </summary>
public enum PacketType : byte
{
    /// <summary>C→S 版本协商，不符直接断开。</summary>
    Hello,

    /// <summary>S→C 分配身份（ClientId / PlayerNetId / ServerTick）。</summary>
    Welcome,

    /// <summary>S→C 全量快照：加入对齐 / 周期纠偏 / 回退重建。</summary>
    Keyframe,

    /// <summary>S→C 常态增量，相对该连接上次 ACK 的基线生成。</summary>
    Delta,

    /// <summary>C→S 回执，丢一两个无妨。</summary>
    Ack,

    /// <summary>双向，优雅断开。</summary>
    Bye,
}
