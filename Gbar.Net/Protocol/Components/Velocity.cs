namespace Gbar.Net.Protocol.Components;

/// <summary>速度组件（米/秒）。无此组件的实体视为静止。</summary>
[SyncComponent]
public struct Velocity
{
    public float X;
    public float Y;
    public float Z;
}
