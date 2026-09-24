namespace Gbar.Core.Net;

/// <summary>
/// 网络事件类型
/// </summary>
public enum NetEventKind
{
    /// <summary>客户端已连接</summary>
    Connected,
    /// <summary>收到客户端数据包</summary>
    Packet,
    /// <summary>客户端已断开</summary>
    Disconnected,
}

/// <summary>
/// 网络事件，由服务器线程产生，供游戏主线程消费
/// </summary>
public readonly struct NetEvent
{
    /// <summary>事件类型</summary>
    public readonly NetEventKind Kind;
    /// <summary>触发事件的客户端 ID</summary>
    public readonly int ClientId;
    /// <summary>数据包内容（仅 <see cref="NetEventKind.Packet"/> 事件有效）</summary>
    public readonly byte[]? Data;
    /// <summary>断开原因（仅 <see cref="NetEventKind.Disconnected"/> 事件有效）</summary>
    public readonly string? Reason;

    private NetEvent(NetEventKind kind, int clientId, byte[]? data, string? reason)
    {
        Kind = kind;
        ClientId = clientId;
        Data = data;
        Reason = reason;
    }

    /// <summary>构造连接事件</summary>
    public static NetEvent Connected(int clientId) => new(NetEventKind.Connected, clientId, null, null);
    /// <summary>构造数据包事件</summary>
    public static NetEvent Packet(int clientId, byte[] data) => new(NetEventKind.Packet, clientId, data, null);
    /// <summary>构造断开事件</summary>
    public static NetEvent Disconnected(int clientId, string reason) => new(NetEventKind.Disconnected, clientId, null, reason);
}
