using MemoryPack;

namespace Gbar.Net.Protocol.Packets;

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
