using Gbar.Net.Protocol.Entities;
using Gbar.Net.Protocol.Entities.State;

namespace Gbar.Core.Components.Identity;

/// <summary>
/// 实体类型标签：标记实体的粗粒度分类（玩家 / 敏化人 / 资源点 / 建筑 / 物品 / 战斗特效等）。
/// 所有需要网络同步的可交互实体都应携带本组件；类型在实体创建时确定、终生不变，
/// NetSyncSystem 据此把类型写入 <see cref="EntitySpawn.Type"/> 下发客户端。
/// 细粒度数据（怪物层级、物品种类、资源熟成度等）另用专门组件，不塞进本枚举。
/// </summary>
public struct EntityTypeTag
{
    public EntityType Value;
}
