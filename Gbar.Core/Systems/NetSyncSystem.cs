using Arch.Core;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Net;
using Gbar.Net.Protocol;
using Gbar.Net.Protocol.Entities;
using Gbar.Net.Protocol.Packets;

namespace Gbar.Core.Systems;

/// <summary>
/// 网络同步系统（定步长，Order 120）：每 tick 末按 AOI 可见集向每个连接发送状态。
/// 对未对齐的连接（尚未收到 Ack）每 tick 重发 Keyframe，对已对齐的连接按相对基线 diff 发 Delta。
/// 可见集由 AoiSystem 写入的 <see cref="AoiCell"/> 决定：只同步与玩家处于同一 cell 或相邻 cell 的实体。
/// 周期纠偏：每 <see cref="GameServer.KeyframeIntervalTicks"/> tick 重发一次 Keyframe，各连接按 (tick + ClientId) 错峰。
/// 对齐状态（Synced/BaselineTick）只由客户端 Ack 驱动（NetServerSystem.OnAck），
/// Keyframe 发送本身不置位，避免 Sequenced Delta 抢在 Keyframe 之前造成"基线空洞"。
/// </summary>
[SystemOrder(120)]
public sealed class NetSyncSystem : FixedSystem
{
    /// <summary>同步可见半径（cell 数）。1 = 玩家所在 cell 及其 26 个相邻 cell。</summary>
    private const int VisibilityRadiusCells = 10;

    private static readonly QueryDescription _query = new QueryDescription().WithAll<NetworkId, Position, AoiCell>();

    private readonly List<(EntityState State, AoiCell Cell)> _all = new();
    private readonly List<EntityState> _visible = new();
    private readonly HashSet<int> _visibleIds = new();

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
            // 跳过未完成 Hello 握手的连接（PlayerEntity 未创建）
            if (session.PlayerEntity == Entity.Null) continue;

            BuildVisible(session);

            // 未对齐（尚未收到首次 Ack）：每 tick 重发 Keyframe，直到客户端确认已应用
            if (!session.Synced || ShouldSendKeyframe(session, tick))
            {
                SendKeyframe(net, session, tick);
                continue;
            }

            SendDelta(net, session, tick);
        }
    }

    /// <summary>发送全量 Keyframe（按可见集过滤）并重置该连接的 diff 基线。</summary>
    private void SendKeyframe(NetServer net, ClientSession session, int tick)
    {
        net.Send(session.ClientId, PacketType.Keyframe, new KeyframePacket
        {
            Tick = tick,
            Entities = _visible.ToArray(),
        });
        session.LastKeyframeTick = tick;
        session.LastSent.Clear();
        foreach (var st in _visible) session.LastSent[st.NetId] = st;
    }

    /// <summary>是否该发周期纠偏 Keyframe：各连接按 (tick + ClientId) 取模错峰。</summary>
    private bool ShouldSendKeyframe(ClientSession session, int tick)
    {
        var interval = Server.KeyframeIntervalTicks;
        if (interval <= 0) return false;
        return tick - session.LastKeyframeTick >= interval && (tick + session.ClientId) % interval == 0;
    }

    /// <summary>收集本 tick 所有同步实体（NetworkId + Position + AoiCell）。</summary>
    private void Collect()
    {
        _all.Clear();
        var world = Server.World;

        var count = world.CountEntities(in _query);
        var entities = new Entity[count];
        world.GetEntities(in _query, entities.AsSpan());
        foreach (var e in entities)
        {
            var id = world.Get<NetworkId>(e).Value;
            var pos = world.Get<Position>(e);
            var vel = world.TryGet<Velocity>(e, out var v) ? v : default;
            var type = world.TryGet<EntityTypeTag>(e, out var tag) ? tag.Value : EntityType.None;
            _all.Add((new EntityState
            {
                NetId = id,
                Type = type,
                PosX = pos.X, PosY = pos.Y, PosZ = pos.Z,
                VelX = vel.X, VelY = vel.Y, VelZ = vel.Z,
            }, world.Get<AoiCell>(e)));
        }
    }

    /// <summary>按玩家 AoiCell 过滤出可见集（同一 cell 或相邻 cell）。</summary>
    private void BuildVisible(ClientSession session)
    {
        var playerCell = Server.World.Get<AoiCell>(session.PlayerEntity);
        _visible.Clear();
        foreach (var (state, cell) in _all)
        {
            if (cell.Within(playerCell, VisibilityRadiusCells))
                _visible.Add(state);
        }
    }

    private void SendDelta(NetServer net, ClientSession session, int tick)
    {
        var spawned = new List<EntityState>();
        var updated = new List<EntityDelta>();
        var despawned = new List<int>();

        _visibleIds.Clear();
        foreach (var st in _visible) _visibleIds.Add(st.NetId);

        foreach (var st in _visible)
        {
            if (!session.LastSent.TryGetValue(st.NetId, out var old))
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
            if (!_visibleIds.Contains(netId)) despawned.Add(netId);

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
        foreach (var st in _visible) session.LastSent[st.NetId] = st;
    }

    /// <summary>按字段比较，生成只含变化字段的增量；无变化返回 <see cref="EntityDelta.Mask"/> = 0。</summary>
    private static EntityDelta DiffEntity(in EntityState cur, in EntityState old)
    {
        var d = new EntityDelta { NetId = cur.NetId };
        byte mask = 0;
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
