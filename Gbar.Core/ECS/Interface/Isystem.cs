using Arch.Core;

namespace Gbar.Core.ECS;

/// <summary>
/// ECS 系统接口。所有逻辑系统均需实现此接口，并由 <see cref="SystemGroup"/> 统一驱动。
/// </summary>
public interface ISystem
{
    /// <summary>
    /// 系统启动时调用，用于初始化对 Arch ECS World 与游戏上下文的引用。
    /// </summary>
    /// <param name="world">Arch ECS 世界实例。</param>
    /// <param name="context">游戏上下文，提供全局共享服务。</param>
    void Start(World world, IGameContext context);

    /// <summary>
    /// 每帧更新时调用。
    /// </summary>
    /// <param name="deltaTime">距离上一帧的时间间隔（秒）。</param>
    void Update(float deltaTime);

    /// <summary>
    /// 系统销毁时调用，用于释放资源或清理状态。
    /// </summary>
    void Destroy();
}
