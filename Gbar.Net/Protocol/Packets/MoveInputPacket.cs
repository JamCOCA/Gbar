using LiteNetLib;
using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

[Flags]
public enum MoveFlags : byte { None = 0, Running = 1, Jumping = 2, Crouch = 4 }


/// <summary>C→S 客户端输入请求。</summary>
[Packet(PacketType.MoveInput, DeliveryMethod.Unreliable, PacketDirection.ClientToServer)]
[MemoryPackable]
public partial struct MoveInputPacket
{
    public float X, Y;          // 输入轴/方向
    public MoveFlags Flags;     // running / jumping / crouch
}
