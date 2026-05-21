using Arch;
using Arch.Core;

namespace Gbar.Core.ECS;

public static class EcsWorldManager
{
    private static World? _world;

    public static World World => _world ?? throw new InvalidOperationException("ECS World 尚未初始化，请先调用 Initialize()");

    public static bool IsInitialized => _world is not null;

    public static void Initialize()
    {
        _world ??= World.Create();
    }

    public static void Dispose()
    {
        _world?.Dispose();
        _world = null;
    }
}
