namespace Gbar.Net.Protocol.Components;

/// <summary>生命组件：当前值 / 上限。无此组件的实体视为不可受损。</summary>
[SyncComponent]
public struct Health
{
    public int Current;
    public int Max;
}
