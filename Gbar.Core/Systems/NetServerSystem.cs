using Arch.Core;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Logging;
using Gbar.Core.Net;
using Gbar.Net.Protocol;
using Gbar.Net.Protocol.Entities;
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

        session.PlayerNetId = registry.AllocNetId();
        session.PlayerEntity = CreatePlayerEntity(session.PlayerNetId);

        net.Send(clientId, PacketType.Welcome, new WelcomePacket
        {
            ClientId = clientId,
            TickRate = Server.TickRate,
            ServerTick = (int)Server.Tick,
            PlayerNetId = session.PlayerNetId,
        });

        Log.Info($"客户端 {clientId} 握手完成，分配玩家 NetId={session.PlayerNetId}");
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
    public void OnKeyframe(KeyframePacket packet) { }
    public void OnDelta(DeltaPacket packet) { }

    // <summary>创建玩家Entity，后续修改加载方式，目前直接世界原点初始化</summary>
    private Entity CreatePlayerEntity(int netId)
    {
        var world = Server.World;
        var entity = world.Create();
        world.Add(entity, new PlayerTag());
        world.Add(entity, new EntityTypeTag { Value = EntityType.Player });
        world.Add(entity, new NetworkId { Value = netId });
        world.Add(entity, new Position());   // 出生在世界原点，后续由出生点/存档系统决定
        world.Add(entity, new Velocity());
        world.Add(entity, new AoiCell());    // AOI 标签由 AoiSystem 每 tick 现算更新
        return entity;
    }

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
}
