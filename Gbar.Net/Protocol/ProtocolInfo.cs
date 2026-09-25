namespace Gbar.Net.Protocol;

/// <summary>
/// 协议标准常量。版本号是协议契约的一部分，定义在协议层供服务器与客户端共用
/// （此前在 NetServer 上，拆包后协议层不再依赖传输层，常量随之迁入）。
/// </summary>
public static class ProtocolInfo
{
    /// <summary>协议版本号，用于客户端/服务器兼容性校验</summary>
    public const int Version = 3;

    /// <summary>
    /// 数据包层共享密钥（XOR 混淆，非强加密）。服务器与客户端必须使用完全相同的密钥，
    /// 否则握手阶段即失败。定义在协议层供两端共用。
    /// </summary>
    public const string PacketLayerKey = "gbar-packet-layer-key-v1";
}
