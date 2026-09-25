namespace Gbar.Core.Components.Status;

/// <summary>
/// 生命健康度（血条）：肉体组织与循环系统的储备值。
/// 承受敌方伤害与自残施法共同消耗；≤0 视为死亡/濒死（具体表现由系统决定）。
/// </summary>
public struct Health
{
    /// <summary>当前生命值。</summary>
    public int Current;

    /// <summary>生命值上限。</summary>
    public int Max;
}
