using Arch.Core;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Net;
using Gbar.Net.Protocol;
using Gbar.Net.Server;

namespace Gbar.Core.Systems;

/// <summary>
/// 网络同步系统（定步长，Order 120）：每 tick 末收集所有同步实体的状态，
/// 对未对齐的连接发 Keyframe，对已对齐的连接按相对基线 diff 发 Delta。
/// 周期纠偏：每 <see cref="GameServer.KeyframeIntervalTicks"/> tick 重发一次 Keyframe，
/// 各连接按 (tick + ClientId) 取模错开相位，平均每 tick 只有 1/interval 的连接收 Keyframe。
/// 天体（Celestial）不在此列——其位置由客户端按根数 + tick 自行解析（见 GbarNet.md）。
/// </summary>
[SystemOrder(120)]
public sealed class NetSyncSystem : FixedSystem
{
    private static readonly QueryDescription _globalQuery = new QueryDescription().WithAll<NetworkId, GlobalPosition>();
    private static readonly QueryDescription _localQuery = new QueryDescription().WithAll<NetworkId, ParentRef, LocalPosition>();

    private readonly Dictionary<int, EntityState> _current = new();

    public NetSyncSystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        var net = Server.NetServer;
        var registry = Server.ClientRegistry;
        if (net is null || registry is null) return;
        if (registry.Sessions.Count == 0) return;

        Collect();
        var tick = (int)Server.Tick;

        foreach (var session in registry.Sessions.Values)
        {
            // 跳过未完成 Hello 握手的连接（PlayerEntity 未创建），
            // 否则 Keyframe 会抢在 Welcome 之前发出，且快照里还没有玩家实体
            if (session.PlayerEntity == Entity.Null) continue;

            if (ShouldSendKeyframe(session, tick))
            {
                net.Send(session.ClientId, PacketType.Keyframe, new KeyframePacket
                {
                    Tick = tick,
                    Entities = _current.Values.ToArray(),
                });
                session.Synced = true;
                session.BaselineTick = tick;
                session.LastKeyframeTick = tick;
                session.LastSent.Clear();
                foreach (var (netId, st) in _current) session.LastSent[netId] = st;
                continue;
            }

            SendDelta(net, session, tick);
        }
    }

    /// <summary>是否该发 Keyframe：首次对齐必发；之后按间隔周期纠偏，相位按 ClientId 错开。</summary>
    private bool ShouldSendKeyframe(ClientSession session, int tick)
    {
        if (!session.Synced) return true;
        var interval = Server.KeyframeIntervalTicks;
        if (interval <= 0) return false;
        return tick - session.LastKeyframeTick >= interval && (tick + session.ClientId) % interval == 0;
    }

    /// <summary>收集本 tick 所有同步实体的状态（全局域 + 局部域）。</summary>
    private void Collect()
    {
        _current.Clear();
        var world = Server.World;

        var count = world.CountEntities(in _globalQuery);
        var entities = new Entity[count];
        world.GetEntities(in _globalQuery, entities.AsSpan());
        foreach (var e in entities)
        {
            var id = world.Get<NetworkId>(e).Value;
            var pos = world.Get<GlobalPosition>(e);
            var vel = world.TryGet<GlobalVelocity>(e, out var v) ? v : default;
            _current[id] = new EntityState
            {
                NetId = id,
                SpaceNetId = 0,                      // 全局域无父空间
                PosX = pos.X, PosY = pos.Y, PosZ = pos.Z,
                VelX = vel.X, VelY = vel.Y, VelZ = vel.Z,
            };
        }

        count = world.CountEntities(in _localQuery);
        entities = new Entity[count];
        world.GetEntities(in _localQuery, entities.AsSpan());
        foreach (var e in entities)
        {
            var id = world.Get<NetworkId>(e).Value;
            var parent = world.Get<ParentRef>(e).Parent;
            var pos = world.Get<LocalPosition>(e);
            var vel = world.TryGet<LocalVelocity>(e, out var v) ? v : default;
            _current[id] = new EntityState
            {
                NetId = id,
                SpaceNetId = world.Get<NetworkId>(parent).Value,
                PosX = pos.X, PosY = pos.Y, PosZ = pos.Z,
                VelX = vel.X, VelY = vel.Y, VelZ = vel.Z,
            };
        }
    }

    private void SendDelta(NetServer net, ClientSession session, int tick)
    {
        var spawned = new List<EntityState>();
        var updated = new List<EntityState>();
        var despawned = new List<int>();

        foreach (var (netId, st) in _current)
        {
            if (!session.LastSent.TryGetValue(netId, out var old)) spawned.Add(st);
            else if (!Same(old, st)) updated.Add(st);
        }
        foreach (var netId in session.LastSent.Keys)
            if (!_current.ContainsKey(netId)) despawned.Add(netId);

        net.Send(session.ClientId, PacketType.Delta, new DeltaPacket
        {
            BaseTick = (int)session.BaselineTick,
            CurrTick = tick,
            Spawned = spawned.ToArray(),
            Updated = updated.ToArray(),
            Despawned = despawned.ToArray(),
        });

        session.LastSent.Clear();
        foreach (var (netId, st) in _current) session.LastSent[netId] = st;
    }

    private static bool Same(in EntityState a, in EntityState b) =>
        a.SpaceNetId == b.SpaceNetId &&
        a.PosX == b.PosX && a.PosY == b.PosY && a.PosZ == b.PosZ &&
        a.VelX == b.VelX && a.VelY == b.VelY && a.VelZ == b.VelZ;
}
