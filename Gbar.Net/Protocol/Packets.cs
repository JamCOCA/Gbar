using MemoryPack;

namespace Gbar.Net.Protocol;

/// <summary>
/// 单个实体的同步状态，Keyframe/Delta 包体的基本元素。
/// 协议层不引用 ECS 组件，保持 Core 与传输解耦。
/// </summary>
[MemoryPackable]
public partial struct EntityState
{
    public int NetId;
    public float PosX, PosY;
    public float VelX, VelY;
}

/// <summary>C→S 版本协商包，协议版本不符服务器直接断开。</summary>
[MemoryPackable]
public partial struct HelloPacket
{
    public int ProtocolVersion;
}

/// <summary>S→C 身份分配包；PlayerNetId 为服务器为该连接创建的玩家本体实体。</summary>
[MemoryPackable]
public partial struct WelcomePacket
{
    public int ClientId;
    public int TickRate;
    public int ServerTick;
    public int PlayerNetId;
}

/// <summary>S→C 全量快照包：新连接对齐 / 周期纠偏 / 长时间未 ACK 回退重建三种情况发送。</summary>
[MemoryPackable]
public partial struct KeyframePacket
{
    public int Tick;
    public EntityState[] Entities;
}

/// <summary>
/// S→C 增量包（常态）。BaseTick = 该连接上次 ACK 的 tick，即本包相对基线；
/// 客户端校验 BaseTick == 本地 LastAppliedTick，不符则丢弃等 Keyframe。
/// </summary>
[MemoryPackable]
public partial struct DeltaPacket
{
    public int BaseTick;
    public int CurrTick;
    public EntityState[] Spawned;
    public EntityState[] Updated;
    public int[] Despawned;
}

/// <summary>C→S 回执包，携带客户端已应用的最新 tick，作为服务器生成下一个 Delta 的基线。</summary>
[MemoryPackable]
public partial struct AckPacket
{
    public int LastAppliedTick;
}

/// <summary>双向优雅断开包，携带原因便于对端日志与清理。</summary>
[MemoryPackable]
public partial struct ByePacket
{
    public string Reason;
}
