namespace Gbar.Net.Protocol;

/// <summary>
/// 协议标准常量。版本号是协议契约的一部分，定义在协议层供服务器与客户端共用
/// （此前在 NetServer 上，拆包后协议层不再依赖传输层，常量随之迁入）。
/// </summary>
public static class ProtocolInfo
{
    /// <summary>协议版本号，用于客户端/服务器兼容性校验</summary>
    public const int Version = 1;
}
