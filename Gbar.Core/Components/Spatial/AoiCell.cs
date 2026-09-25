namespace Gbar.Core.Components.Spatial;

/// <summary>
/// 实体所在 AOI 网格 cell 坐标（整数格）。由 AoiSystem 每 tick 按 Position 现算写入，
/// NetSyncSystem 据此过滤可见集（只同步与玩家同一 cell 或相邻 cell 的实体）。
/// </summary>
public struct AoiCell
{
    public int X;
    public int Y;
    public int Z;

    /// <summary>判断本 cell 是否在以 other 为中心、radius（格）为半径的邻域内。</summary>
    public bool Within(in AoiCell other, int radius)
    {
        return Math.Abs(X - other.X) <= radius
            && Math.Abs(Y - other.Y) <= radius
            && Math.Abs(Z - other.Z) <= radius;
    }
}
