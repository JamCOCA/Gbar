namespace Gbar.Net.Protocol.Entities;

/// <summary>
/// 实体类型：对世界内所有可交互内容的粗粒度分类，随 EntitySpawn 同步下发到客户端，
/// 客户端据此选择渲染与交互表现，服务器据此做查询过滤、碰撞分层与 AI 目标选择。
/// 值域按大类分段（1~29 角色、30~39 资源点、40~49 建筑、50~59 物品、60~69 战斗特效），
/// 便于后续扩展与按数值范围做类别判断；细粒度数据（怪物层级、物品种类等）放组件，不塞进本枚举。
/// 与 ECS 侧 <c>Gbar.Core.Components.Identity.EntityTypeTag</c> 组件对应：本枚举是协议层共享的分类值，
/// 组件负责在 ECS 里承载该值并供系统查询，二者分属不同程序集/命名空间，互不混淆。
/// </summary>
public enum EntityType : byte
{
    /// <summary>未指定 / 未知（保留位，不应作为真实实体类型下发）。</summary>
    None = 0,

    // ===== 角色 / 单位（1 ~ 29）=====

    /// <summary>玩家：未接受敏化改造（或仅民用改造）的正统人类，地表压迫者。</summary>
    Player = 1,

    /// <summary>敏化人 · 地表游荡者：逃逸弱个体 / 表型退化者，畏光但可耐受微弱日光；数量有限、死亡不刷新。</summary>
    SurfaceWanderer = 10,

    /// <summary>敏化人 · 浅层个体：低敏化，产量低、战力弱，新人单人安全成长线。</summary>
    ShallowSensitized = 11,

    /// <summary>敏化人 · 中层个体：标准敏化人，有社会组织、会使用工具与战术，组队内容。</summary>
    MidSensitized = 12,

    /// <summary>敏化人 · 深层个体：高敏化，CPC 产量惊人、感知近乎预知，精英团队内容。</summary>
    DeepSensitized = 13,

    /// <summary>零号实验体：第一个改造人、一切敏化人的源头，位面 BOSS，全服合力讨伐。</summary>
    SubjectZero = 14,

    /// <summary>被圈养的敏化人：捕获后投放圈养场的活体资产，持续产出 CPC，可交互（榨取 / 喂养 / 释放）。</summary>
    CaptiveSensitized = 15,

    /// <summary>组织 NPC：学会 / 工坊 / 氏族的成员，承担交易、任务、势力交互（预留）。</summary>
    FactionNpc = 20,

    // ===== 资源点（30 ~ 39）=====

    /// <summary>介质泉：CPC 富集带，占领后持续产出原料（灵脉，按贡献排序分配控制权）。</summary>
    CpcSpring = 30,

    /// <summary>活性结晶矿床：成核 → 生长 → 熟成的成长型资源点，早采活性低、晚采有被抢风险。</summary>
    ActiveCrystalDeposit = 31,

    /// <summary>前文明反应炉遗迹：与介质泉同源的探索 / 占领资源点。</summary>
    ReactorRuin = 32,

    // ===== 建筑 / 设施（40 ~ 49）=====

    /// <summary>圈养场：遮光圈养场，宗门级核心资产，PVP 与夜袭劫掠的首要目标。</summary>
    CaptiveFarm = 40,

    /// <summary>通用建筑：遮光结构、维生系统、防逃脱设施、防御工事等。</summary>
    Structure = 41,

    // ===== 物品 / 掉落（50 ~ 59）=====

    /// <summary>掉落物：击杀敏化人回收的生物质材料（催化腺体、神经组织、富集血液）。</summary>
    LootDrop = 50,

    /// <summary>物品：装备、工业血包、神经补充剂（蓝药）等可拾取 / 可放置物。</summary>
    Item = 51,

    // ===== 战斗 / 法术（60 ~ 69）=====

    /// <summary>法术投射物：气溶胶团、电离束、神经传导枪弹等飞行物。</summary>
    Projectile = 60,

    /// <summary>区域效果：气溶胶场、酸雾（化学沉默）、气压冲击波等范围效应。</summary>
    AreaEffect = 61,
}
