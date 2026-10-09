namespace Gbar.Physics;

/// <summary>
/// 动态/运动学刚体句柄：对 Bepu 的 <c>BodyHandle</c> 做了一层不透明封装，
/// 使 Gbar.Core 无需直接引用 BepuPhysics 类型。<c>Null</c>（Value = -1）表示尚未建体。
/// </summary>
public readonly struct PhysicsBodyHandle : IEquatable<PhysicsBodyHandle>
{
    /// <summary>未建体哨兵句柄。</summary>
    public static PhysicsBodyHandle Null => new(-1);

    /// <summary>底层句柄值（-1 为无效）。</summary>
    public readonly int Value;

    public PhysicsBodyHandle(int value) => Value = value;

    /// <summary>是否为无效句柄（尚未建体）。</summary>
    public bool IsNull => Value == -1;

    public bool Equals(PhysicsBodyHandle other) => other.Value == Value;
    public override bool Equals(object? obj) => obj is PhysicsBodyHandle h && Equals(h);
    public override int GetHashCode() => Value;
    public static bool operator ==(PhysicsBodyHandle a, PhysicsBodyHandle b) => a.Equals(b);
    public static bool operator !=(PhysicsBodyHandle a, PhysicsBodyHandle b) => !a.Equals(b);
}

/// <summary>
/// 静态碰撞体句柄：对 Bepu 的 <c>StaticHandle</c> 的不透明封装。
/// 静态体不写回位姿（不移动），仅在 <see cref="PhysicsWorld"/> 建体/移除时使用。
/// </summary>
public readonly struct PhysicsStaticHandle : IEquatable<PhysicsStaticHandle>
{
    /// <summary>未建体哨兵句柄。</summary>
    public static PhysicsStaticHandle Null => new(-1);

    /// <summary>底层句柄值（-1 为无效）。</summary>
    public readonly int Value;

    public PhysicsStaticHandle(int value) => Value = value;

    /// <summary>是否为无效句柄（尚未建体）。</summary>
    public bool IsNull => Value == -1;

    public bool Equals(PhysicsStaticHandle other) => other.Value == Value;
    public override bool Equals(object? obj) => obj is PhysicsStaticHandle h && Equals(h);
    public override int GetHashCode() => Value;
    public static bool operator ==(PhysicsStaticHandle a, PhysicsStaticHandle b) => a.Equals(b);
    public static bool operator !=(PhysicsStaticHandle a, PhysicsStaticHandle b) => !a.Equals(b);
}
