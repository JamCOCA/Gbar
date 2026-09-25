using Arch.System;
using Arch.System.SourceGenerator;
using Gbar.Core.Components.Spatial;

namespace Gbar.Core.Systems;

/// <summary>
/// AOI 标签系统（定步长，Order 100）：把带 Position 的实体按当前坐标写入其 <see cref="AoiCell"/> 组件。
/// 每 tick 现算 cell 坐标，不维护全局索引；并行安全（每个实体只写自身的组件）。
/// 可见集过滤由 NetSyncSystem 读取 AoiCell 完成。
/// </summary>
[SystemOrder(100)]
public sealed partial class AoiSystem : FixedSystem
{
    /// <summary>网格边长（米）。决定 AOI 邻域的粒度。</summary>
    public const double CellSize = 100.0;

    public AoiSystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        TagQuery(World);
    }

    [Query(Parallel = true)]
    [All<Position, AoiCell>]
    private static void Tag(ref AoiCell cell, in Position position)
    {
        cell.X = (int)Math.Floor(position.X / CellSize);
        cell.Y = (int)Math.Floor(position.Y / CellSize);
        cell.Z = (int)Math.Floor(position.Z / CellSize);
    }
}
