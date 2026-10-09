using Gbar.Physics;

namespace Gbar.Core.Components.Physics;

/// <summary>
/// 静态碰撞体组件（服务器本地，不同步）：把实体挂接到 Bepu 静态碰撞体（不参与动力学，仅作碰撞阻挡）。
/// 由 <see cref="Systems.PhysicsSystem"/> 惰性建体；静态体不移动，因此不回写位姿。
/// </summary>
public struct PhysicsStatic
{
    /// <summary>碰撞形状（盒/球/胶囊/网格）。</summary>
    public PhysicsShape Shape;

    /// <summary>Bepu 静态体句柄；<see cref="PhysicsStaticHandle.Null"/> 表示尚未建体。</summary>
    public PhysicsStaticHandle Handle;

    public PhysicsStatic(in PhysicsShape shape)
    {
        Shape = shape;
        Handle = PhysicsStaticHandle.Null;
    }
}
