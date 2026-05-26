using Arch.Core;
using Gbar.Core.ECS;
using Gbar.Core.ECS.Components;

namespace Gbar.Server.GameThread.Threads;

public class ArchThread : BaseGameThread
{
    public ArchThread()
    {
        TicksPerSecond = 60;
    }

    protected override void OnStart()
    {
        EcsWorldManager.Initialize();
        Debug("Arch ECS 线程已启动");
    }

    protected override void OnStop()
    {
        EcsWorldManager.Dispose();
        Debug("Arch ECS 线程已停止");
    }

    protected override void Update(float deltaTime)
    {
        var world = EcsWorldManager.World;

    
    }
}
