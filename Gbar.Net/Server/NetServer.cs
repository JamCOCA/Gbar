using System.Collections.Concurrent;
using Gbar.Net.Protocol;
using LiteNetLib;

namespace Gbar.Net.Server;

/// <summary>
/// 基于 LiteNetLib 的游戏服务器，负责接收客户端连接和收发数据包。
/// 网络事件在后台轮询线程中产生，通过 <see cref="Events"/> 队列传递给游戏主线程处理。
/// </summary>
public sealed class NetServer : IDisposable
{
    /// <summary>协议版本号，用于客户端/服务器兼容性校验</summary>
    public const int ProtocolVersion = 1;

    /// <summary>待处理的网络事件队列（线程安全）</summary>
    private readonly ConcurrentQueue<NetEvent> _events = new();
    /// <summary>已连接的客户端，键为客户端 ID</summary>
    private readonly ConcurrentDictionary<int, NetPeer> _peers = new();
    private EventBasedNetListener? _listener;
    private NetManager? _manager;
    /// <summary>后台轮询线程</summary>
    private Thread? _pollThread;
    private volatile bool _running;

    /// <summary>网络事件队列，游戏主线程从中取出事件处理</summary>
    public ConcurrentQueue<NetEvent> Events => _events;
    /// <summary>当前已连接的客户端数量</summary>
    public int PeerCount => _peers.Count;

    /// <summary>
    /// 启动服务器并监听指定端口
    /// </summary>
    /// <param name="port">监听端口</param>
    /// <param name="maxConnections">最大连接数</param>
    /// <exception cref="InvalidOperationException">端口绑定失败时抛出</exception>
    public void Start(int port, int maxConnections = 32)
    {
        if (_running) return;

        _listener = new EventBasedNetListener();
        _manager = new NetManager(_listener)
        {
            AutoRecycle = true,
        };

        // 连接请求：校验密钥，不匹配则拒绝
        _listener.ConnectionRequestEvent += request => request.AcceptIfKey("gbar");
        // 客户端连接成功
        _listener.PeerConnectedEvent += peer =>
        {
            _peers[peer.Id] = peer;
            _events.Enqueue(NetEvent.Connected(peer.Id));
        };
        // 客户端断开
        _listener.PeerDisconnectedEvent += (peer, info) =>
        {
            _peers.TryRemove(peer.Id, out _);
            _events.Enqueue(NetEvent.Disconnected(peer.Id, info.Reason.ToString()));
        };
        // 收到客户端数据
        _listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            var data = new byte[reader.AvailableBytes];
            reader.GetBytes(data, data.Length);
            _events.Enqueue(NetEvent.Packet(peer.Id, data));
        };

        if (!_manager.Start(port))
            throw new InvalidOperationException($"NetServer failed to bind port {port}");

        _running = true;
        _pollThread = new Thread(PollLoop) { Name = "Gbar.NetServer", IsBackground = true };
        _pollThread.Start();
    }

    /// <summary>
    /// 向指定客户端发送数据包
    /// </summary>
    /// <typeparam name="T">数据包结构体类型</typeparam>
    /// <param name="clientId">目标客户端 ID</param>
    /// <param name="type">数据包类型</param>
    /// <param name="body">数据包内容</param>
    public void Send<T>(int clientId, PacketType type, in T body) where T : struct
    {
        if (!_peers.TryGetValue(clientId, out var peer)) return;
        peer.Send(PacketCodec.Encode(type, body), PacketCodec.ChannelFor(type));
    }

    /// <summary>
    /// 主动断开指定客户端的连接，断开前会先发送 Bye 包告知原因
    /// </summary>
    /// <param name="clientId">目标客户端 ID</param>
    /// <param name="reason">断开原因</param>
    public void Disconnect(int clientId, string reason)
    {
        if (!_peers.TryGetValue(clientId, out var peer)) return;
        peer.Send(PacketCodec.Encode(PacketType.Bye, new ByePacket { Reason = reason }), DeliveryMethod.ReliableOrdered);
        peer.Disconnect();
    }

    /// <summary>
    /// 停止服务器，等待轮询线程退出并释放所有连接
    /// </summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _pollThread?.Join();
        _pollThread = null;
        _manager?.Stop();
        _manager = null;
        _peers.Clear();
    }

    /// <summary>
    /// 后台轮询循环，持续驱动 LiteNetLib 处理网络事件
    /// </summary>
    private void PollLoop()
    {
        while (_running)
        {
            _manager!.PollEvents();
            Thread.Sleep(1);
        }
    }

    public void Dispose() => Stop();
}
