using Arch.Core;

namespace Gbar.Core.Persistence;

/// <summary>
/// 持久化提供方接口：定义实体世界的整体保存/加载。
/// 当前由 <see cref="JsonPersistenceProvider"/> 实现（JSON 文件），后续可替换为 SQL 等实现而不改动 <see cref="GameServer"/>。
/// </summary>
public interface IPersistenceProvider
{
    /// <summary>将 World 中需要持久化的实体序列化并写入存储。调用方保证此时没有系统正在读写 World。</summary>
    Task SaveAsync(World world, CancellationToken ct = default);

    /// <summary>从存储读取并重建实体到 World。应在主循环启动前调用。</summary>
    Task LoadAsync(World world, CancellationToken ct = default);
}
