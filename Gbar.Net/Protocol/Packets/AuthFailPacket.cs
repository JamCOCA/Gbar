using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

/// <summary>认证失败原因。</summary>
public enum AuthFailReason : byte
{
    Unknown = 0,
    InvalidCredentials = 1,
    AccountExists = 2,
    InvalidInput = 3,
}

/// <summary>S→C 认证失败包，携带失败原因；客户端可据此提示并重试。</summary>
[MemoryPackable]
public partial struct AuthFailPacket
{
    public AuthFailReason Reason;
}
