namespace Gbar.Net.Protocol.Components;

/// <summary>朝向组件：单位四元数（W 为实部）。无此组件的实体视为无朝向。</summary>
[SyncComponent]
public struct Rotation
{
    public float X;
    public float Y;
    public float Z;
    public float W;
    public Rotation()
    {
        X = 0f;
        Y = 0f;
        Z = 0f;
        W =1f;
    }
}
