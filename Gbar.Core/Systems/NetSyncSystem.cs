using Arch.Core;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Net;
using Gbar.Net.Protocol.Components;
using Gbar.Net.Protocol.Entities;
using Gbar.Net.Protocol.Entities.State;
using Gbar.Net.Protocol.Packets;

namespace Gbar.Core.Systems;

/// <summary>
/// 网络同步系统（定步长，Order 120）：每 tick 末按 AOI 可见集向每个连接发送状态。
/// 对未对齐的连接（尚未收到 Ack）每 tick 重发 Keyframe，对已对齐的连接按相对基线 diff 发 Delta。
/// 可见集由 AoiSystem 写入的 <see cref="AoiCell"/> 决定：只同步与玩家处于同一 cell 或相邻 cell 的实体。
/// 周期纠偏：每 <see cref="GameServer.KeyframeIntervalTicks"/> tick 重发一次 Keyframe，各连接按 (tick + ClientId) 错峰。
/// 实体状态按组件标签化负载（<see cref="ComponentEntry"/>）下发：每个实体只携带它实际持有的组件，
/// 投射物/静态物不再背 HP/CPC 等空槽；增量 diff 在组件粒度上进行（组件级增量替代字段级掩码）。
/// 组件采集由源生成器产出的 <see cref="SyncReader"/> 完成，本系统不感知具体组件。
/// 对齐状态（Synced/BaselineTick）只由客户端 Ack 驱动（NetServerSystem.OnAck），
/// Keyframe 发送本身不置位，避免 Sequenced Delta 抢在 Keyframe 之前造成"基线空洞"。
/// </summary>
[SystemOrder(120)]
public sealed class NetSyncSystem : FixedSystem
{
    /// <summary>同步可见半径（cell 数）。1 = 玩家所在 cell 及其 26 个相邻 cell。</summary>
    private const int VisibilityRadiusCells = 10;

    private static readonly QueryDescription _query = new QueryDescription().WithAll<NetworkId, Position, AoiCell>();

    private readonly List<Snapshot> _all = new();
    private readonly List<Snapshot> _visible = new();
    private readonly HashSet<int> _visibleIds = new();
    private readonly List<ComponentEntry> _changed = new();

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

    /// <summary>实体在某 tick 的同步快照：身份 + 类型 + 完整组件列表。</summary>
    private readonly struct Snapshot
    {
        public readonly int NetId;
        public readonly EntityType Type;
        public readonly AoiCell Cell;
        public readonly ComponentEntry[] Components;

        public Snapshot(int netId, EntityType type, AoiCell cell, ComponentEntry[] components)
        {
            NetId = netId;
            Type = type;
            Cell = cell;
            Components = components;
        }
    }

    /// <summary>发送全量 Keyframe（按可见集过滤）并重置该连接的 diff 基线。</summary>
    private void SendKeyframe(NetServer net, ClientSession session, int tick)
    {
        var entities = new EntitySpawn[_visible.Count];
        for (var i = 0; i < _visible.Count; i++)
        {
            var s = _visible[i];
            entities[i] = new EntitySpawn { NetId = s.NetId, Type = s.Type, Components = s.Components };
        }

        net.Send(session.ClientId, new KeyframePacket { Tick = tick, Entities = entities });
        session.LastKeyframeTick = tick;
        session.LastSent.Clear();
        foreach (var s in _visible) session.LastSent[s.NetId] = s.Components;
    }

    /// <summary>是否该发周期纠偏 Keyframe：各连接按 (tick + ClientId) 取模错峰。</summary>
    private bool ShouldSendKeyframe(ClientSession session, int tick)
    {
        var interval = Server.KeyframeIntervalTicks;
        if (interval <= 0) return false;
        return tick - session.LastKeyframeTick >= interval && (tick + session.ClientId) % interval == 0;
    }

    /// <summary>收集本 tick 所有同步实体（NetworkId + Position + AoiCell），组件列表由生成器产出的 <see cref="SyncReader"/> 读取。</summary>
    private void Collect()
    {
        _all.Clear();
        var world = Server.World;

        var count = world.CountEntities(in _query);
        var entities = new Entity[count];
        world.GetEntities(in _query, entities.AsSpan());
        foreach (var e in entities)
        {
            var netId = world.Get<NetworkId>(e).Value;
            var type = world.TryGet<EntityTypeTag>(e, out var tag) ? tag.Value : EntityType.None;
            var cell = world.Get<AoiCell>(e);
            _all.Add(new Snapshot(netId, type, cell, SyncReader.Read(world, e)));
        }
    }

    /// <summary>按玩家 AoiCell 过滤出可见集（同一 cell 或相邻 cell）。</summary>
    private void BuildVisible(ClientSession session)
    {
        var playerCell = Server.World.Get<AoiCell>(session.PlayerEntity);
        _visible.Clear();
        foreach (var s in _all)
        {
            if (s.Cell.Within(playerCell, VisibilityRadiusCells))
                _visible.Add(s);
        }
    }

    private void SendDelta(NetServer net, ClientSession session, int tick)
    {
        var spawned = new List<EntitySpawn>();
        var updated = new List<EntityUpdate>();
        var despawned = new List<int>();

        _visibleIds.Clear();
        foreach (var s in _visible) _visibleIds.Add(s.NetId);

        foreach (var s in _visible)
        {
            if (!session.LastSent.TryGetValue(s.NetId, out var old))
            {
                spawned.Add(new EntitySpawn { NetId = s.NetId, Type = s.Type, Components = s.Components });
            }
            else
            {
                _changed.Clear();
                DiffComponents(s.Components, old, _changed);
                if (_changed.Count > 0)
                    updated.Add(new EntityUpdate { NetId = s.NetId, Components = _changed.ToArray() });
            }
        }
        foreach (var netId in session.LastSent.Keys)
            if (!_visibleIds.Contains(netId)) despawned.Add(netId);

        if (spawned.Count == 0 && updated.Count == 0 && despawned.Count == 0)
            return;

        net.Send(session.ClientId, new DeltaPacket
        {
            BaseTick = (int)session.BaselineTick,
            CurrTick = tick,
            Spawned = spawned.ToArray(),
            Updated = updated.ToArray(),
            Despawned = despawned.ToArray(),
        });

        session.LastSent.Clear();
        foreach (var s in _visible) session.LastSent[s.NetId] = s.Components;
    }

    /// <summary>按组件 ID 比对：新增或变化的组件进入 changed；组件集合在实体生命周期内稳定，无需处理移除。</summary>
    private static void DiffComponents(ComponentEntry[] cur, ComponentEntry[] old, List<ComponentEntry> changed)
    {
        foreach (var c in cur)
        {
            var found = false;
            foreach (var o in old)
            {
                if (o.Id != c.Id) continue;
                found = true;
                if (!ComponentCodecRegistry.Codecs[c.Id].Equals(in o, in c)) changed.Add(c);
                break;
            }
            if (!found) changed.Add(c);
        }
    }
}
