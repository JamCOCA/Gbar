using Arch.Core;
using Gbar.Core.Logging;
using Gbar.Core.Net;
using Gbar.Core.Service;
using Gbar.Net.Protocol;
using Gbar.Net.Protocol.Packets;

namespace Gbar.Core.Systems;

/// <summary>
/// 网络事件系统（变步长）：每帧从 <see cref="NetServer.Events"/> 队列取出连接/数据/断开事件，
/// 在主循环线程上完成握手（Hello → Welcome）、玩家实体创建/销毁、Ack 基线更新。
/// 所有 World 变更都在主循环线程上串行进行，不在并行查询内，因此直接写 World 是安全的（无需 CommandBuffer）。
/// </summary>
[SystemOrder(0)]
public sealed class NetServerSystem : UpdateSystem, IPacketHandler
{
    // TryDispatch 不携带 ClientId，串行分发期间以该字段标记当前数据包的来源
    private int _currentClientId;

    public NetServerSystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        var net = Server.NetServer;
        var registry = Server.ClientRegistry;
        if (net is null || registry is null) return;

        while (net.Events.TryDequeue(out var ev))
        {
            switch (ev.Kind)
            {
                case NetEventKind.Connected:
                    registry.Add(ev.ClientId);
                    Log.Info($"客户端 {ev.ClientId} 已连接");
                    break;

                case NetEventKind.Packet:
                    _currentClientId = ev.ClientId;
                    if (ev.Data is null || !PacketCodec.TryDispatch(ev.Data, this))
                        net.Disconnect(ev.ClientId, "非法封包");
                    break;

                case NetEventKind.Disconnected:
                    OnClientDisconnected(ev.ClientId);
                    break;
            }
        }
    }

    // ---- IPacketHandler：服务器只处理 C→S 方向 ----

    public void OnHello(HelloPacket packet)
    {
        var net = Server.NetServer!;
        var registry = Server.ClientRegistry!;
        var clientId = _currentClientId;

        if (packet.ProtocolVersion != ProtocolInfo.Version)
        {
            net.Disconnect(clientId, $"协议版本不符：{packet.ProtocolVersion}");
            return;
        }
        if (!registry.TryGet(clientId, out var session))
        {
            net.Disconnect(clientId, "未知连接");
            return;
        }

        session.State = ClientState.Helloed;   // 版本通过，进入待认证，等 Auth 包
        Log.Info($"客户端 {clientId} 握手完成（版本 {packet.ProtocolVersion}），等待认证");
    }

    public void OnAuth(AuthPacket packet)
    {
        var net = Server.NetServer!;
        var registry = Server.ClientRegistry!;
        var clientId = _currentClientId;
        var auth = Server.Auth;

        if (auth is null)
        {
            net.Disconnect(clientId, "认证服务未就绪");
            return;
        }
        if (!registry.TryGet(clientId, out var session))
        {
            net.Disconnect(clientId, "未知连接");
            return;
        }
        if (session.State == ClientState.Authenticated) return;   // 已认证，忽略重复
        if (session.State != ClientState.Helloed)
        {
            net.Disconnect(clientId, "未完成握手");
            return;
        }

        // 同步等待 DB（临时方案：阻塞主循环；后续改为异步后台 + 主线程回填）
        var (result, _, _) = packet.Mode == AuthMode.Register
            ? auth.RegisterAsync(packet.Account, packet.Password).GetAwaiter().GetResult()
            : auth.LoginAsync(packet.Account, packet.Password).GetAwaiter().GetResult();

        if (result != AuthResult.Success)
        {
            net.Send(clientId, PacketType.AuthFail, new AuthFailPacket { Reason = ToReason(result) });
            Log.Info($"客户端 {clientId} 认证失败（{result}）");
            return;
        }

        session.PlayerNetId = registry.AllocNetId();
        session.PlayerEntity = Server.EntityFactory!.CreatePlayer(session.PlayerNetId);
        session.State = ClientState.Authenticated;

        net.Send(clientId, PacketType.Welcome, new WelcomePacket
        {
            ClientId = clientId,
            TickRate = Server.TickRate,
            ServerTick = (int)Server.Tick,
            PlayerNetId = session.PlayerNetId,
        });

        Log.Info($"客户端 {clientId} 认证成功，账号={packet.Account}，分配 NetId={session.PlayerNetId}");
    }

    public void OnAck(AckPacket packet)
    {
        if (Server.ClientRegistry!.TryGet(_currentClientId, out var session))
        {
            session.BaselineTick = packet.LastAppliedTick;
            session.Synced = true;   // 首次 Ack 确认 Keyframe 已应用，服务器切换到 Delta
        }
    }

    public void OnBye(ByePacket packet)
    {
        Server.NetServer!.Disconnect(_currentClientId, packet.Reason);
    }

    // S→C 方向，服务器不会收到，留空
    public void OnWelcome(WelcomePacket packet) { }
    public void OnAuthFail(AuthFailPacket packet) { }
    public void OnKeyframe(KeyframePacket packet) { }
    public void OnDelta(DeltaPacket packet) { }

    private void OnClientDisconnected(int clientId)
    {
        var registry = Server.ClientRegistry!;
        if (registry.TryGet(clientId, out var session) && registry.Remove(clientId))
        {
            if (session.PlayerEntity != Entity.Null)
                Server.World.Destroy(session.PlayerEntity);
            Log.Info($"客户端 {clientId} 断开，销毁玩家 NetId={session.PlayerNetId}");
        }
    }

    /// <summary>把认证结果映射为协议层的失败原因。</summary>
    private static AuthFailReason ToReason(AuthResult result) => result switch
    {
        AuthResult.AccountExists => AuthFailReason.AccountExists,
        AuthResult.InvalidInput => AuthFailReason.InvalidInput,
        _ => AuthFailReason.InvalidCredentials,
    };
}
