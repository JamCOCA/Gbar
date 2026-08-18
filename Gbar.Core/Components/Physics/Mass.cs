namespace Gbar.Core.Components.Physics;

/// <summary>
/// 质量，定点存储（kg，缩放 1:100，精度 0.01 kg）。
/// 没有此组件的实体不参与力的积分（着地角色、纯玩法实体由各自系统直接写速度）。
/// 天体不挂此组件，由 OrbitalSystem 走开普勒轨道推进，不做力积分。
/// </summary>
public struct Mass
{
    public long Value;
}
