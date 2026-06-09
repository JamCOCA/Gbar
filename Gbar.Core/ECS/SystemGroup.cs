using Arch.Buffer;
using Arch.Core;

namespace Gbar.Core.ECS;

public class SystemGroup
{
    private readonly List<ISystem> _systems = [];
    private readonly List<CommandBuffer> _commandBuffers = [];

    public IReadOnlyList<ISystem> Systems => _systems;
    public List<CommandBuffer> CommandBuffers => _commandBuffers;

    public void Add(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
    }

    public bool Remove(ISystem system) => _systems.Remove(system);

    public void Clear() => _systems.Clear();

    public void Start(World world)
    {
        foreach (var system in _systems)
            system.Start(world);
    }

    public void Update(float deltaTime)
    {
        foreach (var system in _systems)
            system.Update(deltaTime);

        foreach (var ecb in _commandBuffers)
        {
            ecb.Playback(EcsWorldManager.World, true);
        }

        _commandBuffers.Clear();
    }

    public void Destroy()
    {
        foreach (var system in _systems)
            system.Destroy();
    }
}
