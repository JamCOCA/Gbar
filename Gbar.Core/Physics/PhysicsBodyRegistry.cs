using Arch.Core;
using Gbar.Physics;

namespace Gbar.Core.Physics;

/// <summary>
/// 物理体双向注册表：维护 Entity ↔ 刚体/静态体句柄的双向映射，供碰撞判定与后续查询使用。
/// 由 <see cref="Systems.PhysicsSystem"/> 在建体/删体时同步维护；业务系统可经本表：
/// 正向由实体查句柄、反向由句柄查实体（碰撞回调里拿到的只有句柄，需反查是哪两个实体在碰撞）。
/// 说明：正向（Entity→句柄）其实也能直接读 PhysicsBody/PhysicsStatic 组件，本表的价值主要在反向查询与统一入口。
/// </summary>
public sealed class PhysicsBodyRegistry
{
    // 刚体（动态/运动学）：Entity ↔ PhysicsBodyHandle
    private readonly Dictionary<Entity, PhysicsBodyHandle> _bodyByEntity = new();
    private readonly Dictionary<PhysicsBodyHandle, Entity> _entityByBody = new();

    // 静态体：Entity ↔ PhysicsStaticHandle
    private readonly Dictionary<Entity, PhysicsStaticHandle> _staticByEntity = new();
    private readonly Dictionary<PhysicsStaticHandle, Entity> _entityByStatic = new();

    /// <summary>已登记的刚体数量。</summary>
    public int BodyCount => _bodyByEntity.Count;

    /// <summary>已登记的静态体数量。</summary>
    public int StaticCount => _staticByEntity.Count;

    // ===== 刚体 =====

    /// <summary>登记刚体：建立实体与句柄的双向映射。</summary>
    public void RegisterBody(Entity entity, PhysicsBodyHandle handle)
    {
        _bodyByEntity[entity] = handle;
        _entityByBody[handle] = entity;
    }

    /// <summary>注销刚体（实体销毁或移除 PhysicsBody 组件时调用）。返回是否存在并成功移除。</summary>
    public bool UnregisterBody(Entity entity)
    {
        if (!_bodyByEntity.Remove(entity, out var handle)) return false;
        _entityByBody.Remove(handle);
        return true;
    }

    /// <summary>由实体查刚体句柄。</summary>
    public bool TryGetHandle(Entity entity, out PhysicsBodyHandle handle) => _bodyByEntity.TryGetValue(entity, out handle);

    /// <summary>由刚体句柄查实体（碰撞判定反查用）。</summary>
    public bool TryGetEntity(PhysicsBodyHandle handle, out Entity entity) => _entityByBody.TryGetValue(handle, out entity);

    // ===== 静态体 =====

    /// <summary>登记静态体：建立实体与静态体句柄的双向映射。</summary>
    public void RegisterStatic(Entity entity, PhysicsStaticHandle handle)
    {
        _staticByEntity[entity] = handle;
        _entityByStatic[handle] = entity;
    }

    /// <summary>注销静态体。返回是否存在并成功移除。</summary>
    public bool UnregisterStatic(Entity entity)
    {
        if (!_staticByEntity.Remove(entity, out var handle)) return false;
        _entityByStatic.Remove(handle);
        return true;
    }

    /// <summary>由实体查静态体句柄。</summary>
    public bool TryGetStaticHandle(Entity entity, out PhysicsStaticHandle handle) => _staticByEntity.TryGetValue(entity, out handle);

    /// <summary>由静态体句柄查实体（碰撞判定反查用）。</summary>
    public bool TryGetEntity(PhysicsStaticHandle handle, out Entity entity) => _entityByStatic.TryGetValue(handle, out entity);

    /// <summary>清空全部映射（通常仅在世界重置时调用）。</summary>
    public void Clear()
    {
        _bodyByEntity.Clear();
        _entityByBody.Clear();
        _staticByEntity.Clear();
        _entityByStatic.Clear();
    }
}
