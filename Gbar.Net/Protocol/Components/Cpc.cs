namespace Gbar.Net.Protocol.Components;

/// <summary>CPC 储量组件：当前值 / 上限。无此组件的实体视为无 CPC。</summary>
[SyncComponent]
public struct Cpc
{
    public int Current;
    public int Max;
}
