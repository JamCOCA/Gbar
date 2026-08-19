using Arch.Core;
using Gbar.Net.Protocol;

namespace Gbar.Core.Net;

/// <summary>
/// 单个连接的会话状态。仅在主循环线程上读写，无需加锁。
/// </summary>
public sealed class ClientSession
{
    /// <summary>客户端 ID（LiteNetLib Peer.Id）。</summary>
    public int ClientId;

    /// <summary>服务器分配给该玩家的 NetId。</summary>
    public int PlayerNetId;

    /// <summary>玩家本体实体（PlayerTag + NetworkId + 坐标组件）。</summary>
    public Entity PlayerEntity = Entity.Null;

    /// <summary>是否已发送过 Keyframe。</summary>
    public bool Synced;

    /// <summary>上次发送 Keyframe 的 tick，用于周期纠偏调度。</summary>
    public long LastKeyframeTick;

    /// <summary>该连接已 ACK 的最新 tick，作为 Delta 的 BaseTick 基线。</summary>
    public long BaselineTick;

    /// <summary>上次发给该连接的实体状态（NetId → 状态），用于增量 diff。</summary>
    public Dictionary<int, EntityState> LastSent = new();
}

/// <summary>
/// 连接注册表 + NetId 分配器。仅在主循环线程上读写。
/// </summary>
public sealed class ClientRegistry
{
    private readonly Dictionary<int, ClientSession> _sessions = new();
    private int _nextNetId = 1;

    /// <summary>当前连接会话，键为客户端 ID。</summary>
    public IReadOnlyDictionary<int, ClientSession> Sessions => _sessions;

    /// <summary>分配一个全局唯一的 NetId。</summary>
    public int AllocNetId() => _nextNetId++;

    /// <summary>注册一个新连接。</summary>
    public void Add(int clientId) => _sessions[clientId] = new ClientSession { ClientId = clientId };

    /// <summary>按客户端 ID 查找会话。</summary>
    public bool TryGet(int clientId, out ClientSession session) => _sessions.TryGetValue(clientId, out session!);

    /// <summary>移除会话，返回是否移除成功。</summary>
    public bool Remove(int clientId) => _sessions.Remove(clientId);
}
