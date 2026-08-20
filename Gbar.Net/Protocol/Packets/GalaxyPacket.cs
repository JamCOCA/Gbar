using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

/// <summary>S→C 星图表：Welcome 之后、首个 Keyframe 之前下发一次（ReliableOrdered）。</summary>
[MemoryPackable]
public partial struct GalaxyPacket
{
    public CelestialState[] Bodies;
}
