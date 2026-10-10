namespace Gbar.Physics;

/// <summary>刚体类型：决定 Bepu 里以何种方式创建与参与模拟。</summary>
public enum PhysicsBodyType
{
    /// <summary>动态体：受力/重力/碰撞，质量有限，可被推动。</summary>
    Dynamic,

    /// <summary>运动学体：质量视为无穷大，不受力、不被推动，但可手动设速度带动其他物体。</summary>
    Kinematic,

    /// <summary>静态体：不参与动力学积分，仅作碰撞阻挡。请使用 <see cref="PhysicsStaticHandle"/> 而非刚体句柄。</summary>
    Static,
}

/// <summary>碰撞形状类型（盒体/球体/胶囊体/三角网格）。</summary>
public enum PhysicsShapeType
{
    /// <summary>轴对齐参数化的盒体（由半尺寸描述，朝向由刚体位姿决定）。</summary>
    Box,

    /// <summary>球体（由半径描述）。</summary>
    Sphere,

    /// <summary>胶囊体（球帽 + 圆柱中段，适合角色/子弹等长条物体）。</summary>
    Capsule,

    /// <summary>三角网格（任意复杂度的静态几何；开销大，动态体慎用）。</summary>
    Mesh,
}

/// <summary>
/// 按轴旋转锁定（<see cref="PhysicsWorld.CreateBody"/> 的 Dynamic 专用）：
/// 被锁轴的逆惯量对角分量置零（转动惯量视为无穷大），碰撞/冲量不会使刚体绕该轴旋转。
/// 用法同 Unity Rigidbody 的 Freeze Rotation XYZ；角色通常锁 <see cref="X"/>|<see cref="Z"/> 只留 Y 转向。
/// </summary>
[Flags]
public enum RotationLock
{
    /// <summary>不锁定：三轴均可自由旋转。</summary>
    None = 0,

    /// <summary>锁定绕局部 X 轴旋转（前后俯仰）。</summary>
    X = 1,

    /// <summary>锁定绕局部 Y 轴旋转（左右转向）。</summary>
    Y = 2,

    /// <summary>锁定绕局部 Z 轴旋转（左右侧倾）。</summary>
    Z = 4,
}
