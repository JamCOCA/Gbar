namespace Gbar.Core.Components.Spatial;

/// <summary>
/// 实体在世界中的位置（米）。单一世界坐标系，服务器权威，双精度浮点。
/// 地表 + 地下统一用同一坐标系，不再区分全局域/局部域。
/// </summary>
public struct Position
{
    public double X;
    public double Y;
    public double Z;
}
