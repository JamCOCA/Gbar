namespace Gbar.Core.Components.Status;

/// <summary>
/// 催化介质储量（蓝条，CPC）：神经系统内可立即动用的有效递质浓度，决定施法频率与维持时长。
/// 施法消耗，随神经再合成 / 回蓝道具缓慢恢复。
/// </summary>
public struct Cpc
{
    /// <summary>当前 CPC 储量。</summary>
    public int Current;

    /// <summary>CPC 储量上限。</summary>
    public int Max;
}
