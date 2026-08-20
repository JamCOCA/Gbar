using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

/// <summary>C→S 回执包，携带客户端已应用的最新 tick，作为服务器生成下一个 Delta 的基线。</summary>
[MemoryPackable]
public partial struct AckPacket
{
    public int LastAppliedTick;
}
