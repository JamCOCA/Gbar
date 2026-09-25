namespace Gbar.Core.Components.Spatial;

/// <summary>
/// 实体朝向（单位四元数，W 为实部）。
/// 表示从基准朝向到当前朝向的旋转；恒等朝向为 (0, 0, 0, 1)。
/// X/Y/Z/W 分量顺序与 Godot/Unity 等客户端约定一致，便于直接互通；连续量用 float（仅位置用 double）。
/// 无此组件的实体视为无朝向（如点状资源点 / 特效）。
/// </summary>
public struct Rotation
{
    public float X;
    public float Y;
    public float Z;
    public float W;
}
