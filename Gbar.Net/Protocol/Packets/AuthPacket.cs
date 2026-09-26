using LiteNetLib;
using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

/// <summary>认证模式：注册 / 登录。</summary>
public enum AuthMode : byte
{
    Register = 0,
    Login = 1,
}

/// <summary>C→S 认证包：注册或登录（由 <see cref="Mode"/> 区分），握手后、Welcome 前发送。</summary>
[Packet(PacketType.Auth, DeliveryMethod.ReliableOrdered, PacketDirection.ClientToServer)]
[MemoryPackable]
public partial struct AuthPacket
{
    public AuthMode Mode;
    public string Account;
    public string Password;
}
