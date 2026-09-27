namespace Gbar.Net.Protocol.Components;

/// <summary>位置组件：世界坐标（米，双精度）。所有可同步实体都应携带。</summary>
[SyncComponent]
public struct Position
{
    public double X;
    public double Y;
    public double Z;
}
