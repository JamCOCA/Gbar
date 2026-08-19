using MemoryPack;

namespace Gbar.Net.Protocol;

/// <summary>
/// 单个实体的同步状态，Keyframe/Delta 包体的基本元素。
/// 协议层不引用 ECS 组件，保持 Core 与传输解耦。
/// 坐标/速度为定点 long（1:100，见 GbarMotion.md），不用 float——
/// 星系级坐标超出 float 精度，且定点保证两端逐 bit 一致。
/// </summary>
[MemoryPackable]
public partial struct EntityState
{
    public int NetId;                   // 4 B
    public int SpaceNetId;              // 4 B  所属空间（ParentRef 指向实体的 NetId）；0 = 全局域
    public long PosX, PosY, PosZ;       // 24 B 定点位置（所在域坐标系）
    public long VelX, VelY, VelZ;       // 24 B 定点速度（着地实体由位置差分现算填充）
}
// 合计 56 B/实体（MemoryPack 头部按 ~2 B 摊销，按 58 B 估算）

/// <summary>
/// 天体静态定义：星图数据，入网时一次性下发（GalaxyPacket），之后永不逐 tick 同步。
/// 天体位置是自变量为 tick 的纯函数，两端各自按轨道根数解析求值。
/// </summary>
[MemoryPackable]
public partial struct CelestialState
{
    public int NetId;
    public int ParentNetId;             // 母天体 NetId；-1 = 恒星（轨道中心）
    public double Mu;                   // 引力参数 μ = G·M
    public double SurfaceRadius;
    public double SoiRadius;
    public float AngularVelocity;       // 自转角速度（rad/tick）
    public float InitialRotation;       // tick 0 时的自转角；Rotation = InitialRotation + AngularVelocity × tick
    public double SemiMajorAxis;        // 轨道根数（恒星忽略）
    public double Eccentricity;
    public double OrbitalPeriod;
    public double InitialPhase;
}

/// <summary>S→C 星图表：Welcome 之后、首个 Keyframe 之前下发一次（ReliableOrdered）。</summary>
[MemoryPackable]
public partial struct GalaxyPacket
{
    public CelestialState[] Bodies;
}

/// <summary>C→S 版本协商包，协议版本不符服务器直接断开。</summary>
[MemoryPackable]
public partial struct HelloPacket
{
    public int ProtocolVersion;
}

/// <summary>S→C 身份分配包；PlayerNetId 为服务器为该连接创建的玩家本体实体。ServerTick 用于两端 tick 对齐（天体轨道求值的时间基准）。</summary>
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
/// 单包若超过 Sequenced 通道上限，由传输层自动分片（见 PacketCodec/FragmentAssembler），本协议无需感知。
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
