using System.Threading;
using Gbar.Net.Protocol;
using LiteNetLib;

namespace Gbar.Net.Client;

/// <summary>
/// 基于 LiteNetLib 的游戏客户端封装，负责连接服务器并收发数据包。
/// 网络事件在后台轮询线程中处理，通过事件回调与 <see cref="Handler"/> 通知上层。
/// </summary>
public sealed class NetClient : IDisposable
{
    private EventBasedNetListener? _listener;
    private NetManager? _manager;
    /// <summary>与服务器连接的 Peer，未连接时为 null</summary>
    private NetPeer? _peer;
    /// <summary>后台轮询线程</summary>
    private Thread? _pollThread;
    private volatile bool _running;
    /// <summary>上行分片消息序号（全局递增）。</summary>
    private ushort _nextMessageId = 1;
    /// <summary>下行分片重组器（轮询线程单线程访问）。</summary>
    private readonly FragmentAssembler _assembler = new();
    /// <summary>累计发送字节数（应用层 payload，不含 LiteNetLib 包头）。</summary>
    private long _bytesSent;
    /// <summary>累计接收字节数（应用层 payload，不含 LiteNetLib 包头）。</summary>
    private long _bytesReceived;

    /// <summary>数据包处理器，收到服务器数据包时通过 <see cref="PacketCodec.TryDispatch"/> 分发给它</summary>
    public IPacketHandler? Handler { get; set; }
    /// <summary>连接成功时触发（在后台轮询线程中调用）</summary>
    public event Action? Connected;
    /// <summary>连接断开时触发，参数为断开原因（在后台轮询线程中调用）</summary>
    public event Action<string>? Disconnected;
    /// <summary>当前是否已连接到服务器</summary>
    public bool IsConnected => _peer is { ConnectionState: ConnectionState.Connected };
    /// <summary>累计发送字节数（线程安全，供上层统计上行速率）。</summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);
    /// <summary>累计接收字节数（线程安全，供上层统计下行速率）。</summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>
    /// 连接到指定服务器
    /// </summary>
    /// <param name="host">服务器地址</param>
    /// <param name="port">服务器端口</param>
    public void Connect(string host, int port)
    {
        if (_running) return;

        _listener = new EventBasedNetListener();
        _manager = new NetManager(_listener)
        {
            AutoRecycle = true,
        };

        // 连接成功
        _listener.PeerConnectedEvent += peer =>
        {
            _peer = peer;
            Connected?.Invoke();
        };
        // 连接断开
        _listener.PeerDisconnectedEvent += (peer, info) =>
        {
            _peer = null;
            Disconnected?.Invoke(info.Reason.ToString());
        };
        // 收到服务器数据，重组（分片）后解码分发给 Handler
        _listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            if (Handler is null) return;
            var data = new byte[reader.AvailableBytes];
            reader.GetBytes(data, data.Length);
            Interlocked.Add(ref _bytesReceived, data.Length);
            var frame = _assembler.Accept(data);   // 分片则重组，未收齐返回 null
            if (frame is not null)
                PacketCodec.TryDispatch(frame, Handler);
        };

        _manager.Start();
        _manager.Connect(host, port, "gbar");

        _running = true;
        _pollThread = new Thread(PollLoop) { Name = "Gbar.NetClient", IsBackground = true };
        _pollThread.Start();
    }

    /// <summary>发送 Hello 握手包，携带协议版本号</summary>
    public void SendHello() => Send(PacketType.Hello, new HelloPacket { ProtocolVersion = Server.NetServer.ProtocolVersion });

    /// <summary>发送 Ack 确认包，告知服务器已应用到哪个 Tick</summary>
    public void SendAck(int lastAppliedTick) => Send(PacketType.Ack, new AckPacket { LastAppliedTick = lastAppliedTick });

    /// <summary>
    /// 向服务器发送数据包；未连接时静默忽略
    /// </summary>
    /// <typeparam name="T">数据包结构体类型</typeparam>
    /// <param name="type">数据包类型</param>
    /// <param name="body">数据包内容</param>
    public void Send<T>(PacketType type, in T body) where T : struct
    {
        if (_peer is null) return;
        var method = PacketCodec.ChannelFor(type);
        var frame = PacketCodec.EncodeFrame(type, body);
        var maxSize = _peer.GetMaxSinglePacketSize(method);

        if (frame.Length <= maxSize)
        {
            _peer.Send(frame, method);
            Interlocked.Add(ref _bytesSent, frame.Length);
            return;
        }

        foreach (var fragment in PacketCodec.SplitFrame(frame, maxSize, _nextMessageId++))
        {
            _peer.Send(fragment, method);
            Interlocked.Add(ref _bytesSent, fragment.Length);
        }
    }

    /// <summary>
    /// 主动断开连接，断开前先发送 Bye 包告知服务器原因
    /// </summary>
    /// <param name="reason">断开原因</param>
    public void Disconnect(string reason)
    {
        Send(PacketType.Bye, new ByePacket { Reason = reason });
        _peer?.Disconnect();
    }

    /// <summary>
    /// 停止客户端，等待轮询线程退出并释放连接
    /// </summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _pollThread?.Join();
        _pollThread = null;
        _manager?.Stop();
        _manager = null;
        _peer = null;
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
