using Arch.Core;
using Gbar.Core.Components.Celestial;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Net;
using Gbar.Net.Protocol;
using Gbar.Net.Protocol.Packets;
using Gbar.Net.Server;

namespace Gbar.Core.Systems;

/// <summary>
/// 网络同步系统（定步长，Order 120）：每 tick 末收集所有同步实体的状态，
/// 对未对齐的连接（尚未收到 Ack）每 tick 重发 Keyframe，对已对齐的连接按相对基线 diff 发 Delta。
/// 周期纠偏：每 <see cref="GameServer.KeyframeIntervalTicks"/> tick 重发一次 Keyframe，
/// 各连接按 (tick + ClientId) 取模错开相位，平均每 tick 只有 1/interval 的连接收 Keyframe。
/// 对齐状态（Synced/BaselineTick）只由客户端 Ack 驱动（NetServerSystem.OnAck），
/// Keyframe 发送本身不置位，避免 Sequenced Delta 抢在 Keyframe 之前造成"基线空洞"。
/// 天体（Celestial）不在此列——其位置由客户端按根数 + tick 自行解析（见 GbarNet.md）。
/// </summary>
[SystemOrder(120)]
public sealed class NetSyncSystem : FixedSystem
{
    private static readonly QueryDescription _globalQuery = new QueryDescription().WithAll<NetworkId, GlobalPosition>().WithNone<Celestial>();
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

            // 未对齐（尚未收到首次 Ack）：每 tick 重发 Keyframe，直到客户端确认已应用。
            // 否则 Sequenced 的 Delta 会抢在 ReliableOrdered 的 Keyframe 之前到达，
            // 客户端以空基线丢包（"基线空洞"）。
            if (!session.Synced)
            {
                SendKeyframe(net, session, tick);
                continue;
            }

            if (ShouldSendKeyframe(session, tick))
            {
                SendKeyframe(net, session, tick);
                continue;
            }

            SendDelta(net, session, tick);
        }
    }

    /// <summary>发送全量 Keyframe 并重置该连接的 diff 基线；对齐状态（Synced/BaselineTick）由 Ack 驱动（见 NetServerSystem.OnAck）。</summary>
    private void SendKeyframe(NetServer net, ClientSession session, int tick)
    {
        net.Send(session.ClientId, PacketType.Keyframe, new KeyframePacket
        {
            Tick = tick,
            Entities = _current.Values.ToArray(),
        });
        session.LastKeyframeTick = tick;
        session.LastSent.Clear();
        foreach (var (netId, st) in _current) session.LastSent[netId] = st;
    }

    /// <summary>是否该发周期纠偏 Keyframe：按间隔 + ClientId 相位错开。</summary>
    private bool ShouldSendKeyframe(ClientSession session, int tick)
    {
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
        var updated = new List<EntityDelta>();
        var despawned = new List<int>();

        foreach (var (netId, st) in _current)
        {
            if (!session.LastSent.TryGetValue(netId, out var old))
            {
                spawned.Add(st);
            }
            else
            {
                var delta = DiffEntity(st, old);
                if (delta.Mask != 0) updated.Add(delta);
            }
        }
        foreach (var netId in session.LastSent.Keys)
            if (!_current.ContainsKey(netId)) despawned.Add(netId);

        if (spawned.Count == 0 && updated.Count == 0 && despawned.Count == 0)
            return;

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

    /// <summary>按字段比较，生成只含变化字段的增量；无变化返回 <see cref="EntityDelta.Mask"/> = 0。</summary>
    private static EntityDelta DiffEntity(in EntityState cur, in EntityState old)
    {
        var d = new EntityDelta { NetId = cur.NetId };
        byte mask = 0;
        if (cur.SpaceNetId != old.SpaceNetId) { mask |= (byte)EntityFields.SpaceNetId; d.SpaceNetId = cur.SpaceNetId; }
        if (cur.PosX != old.PosX) { mask |= (byte)EntityFields.PosX; d.PosX = cur.PosX; }
        if (cur.PosY != old.PosY) { mask |= (byte)EntityFields.PosY; d.PosY = cur.PosY; }
        if (cur.PosZ != old.PosZ) { mask |= (byte)EntityFields.PosZ; d.PosZ = cur.PosZ; }
        if (cur.VelX != old.VelX) { mask |= (byte)EntityFields.VelX; d.VelX = cur.VelX; }
        if (cur.VelY != old.VelY) { mask |= (byte)EntityFields.VelY; d.VelY = cur.VelY; }
        if (cur.VelZ != old.VelZ) { mask |= (byte)EntityFields.VelZ; d.VelZ = cur.VelZ; }
        d.Mask = mask;
        return d;
    }
}
