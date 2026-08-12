using Arch.Buffer;
using Arch.Core;

namespace Gbar.Core;

/// <summary>
/// CommandBuffer 持有者：系统在并行查询中通过 <see cref="Current"/> 记录结构性操作，
/// 由 <see cref="GameServer"/> 在系统组更新结束后统一 <see cref="Playback"/>。
/// </summary>
public sealed class CommandBufferHub : IDisposable
{
    /// <summary>当前帧使用的缓冲区，线程安全，可在 ParallelQuery 中写入。</summary>
    public CommandBuffer Current { get; } = new();

    /// <summary>回放所有缓存的操作到世界并清空记录，缓冲区可继续复用。只能在主循环线程调用。</summary>
    public void Playback(World world) => Current.Playback(world, dispose: true);

    public void Dispose() => Current.Dispose();
}
