namespace Gbar.Net.Protocol;

/// <summary>
/// 应用层协议包类型，占封包首字节（1 byte）。
/// 新增包类型时：在此加枚举成员，并在对应结构体上标注
/// <see cref="PacketAttribute"/>，再于对应端 <see cref="Tools.PacketDispatcher"/> 注册处理器即可；
/// 未注册的包会被静默丢弃（Dispatch 返回 false）。
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

    /// <summary>C→S 注册/登录（双功能，由 AuthPacket.Mode 区分）。</summary>
    Auth,

    /// <summary>S→C 认证失败，携带原因。</summary>
    AuthFail,
}
