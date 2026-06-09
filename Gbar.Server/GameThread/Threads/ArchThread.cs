using Arch.Core;
using Gbar.Core.ECS;
using Gbar.Core.ECS.Components;

namespace Gbar.Server.GameThread.Threads;

public class ArchThread : BaseGameThread
{
    public List<SystemGroup> SystemGroups;
    public ArchThread()
    {
        TicksPerSecond = 60;
        SystemGroups = [];
    }

    protected override void OnStart()
    {
        EcsWorldManager.Initialize();
        Debug("Arch ECS 线程已启动");
        var world = EcsWorldManager.World;

        // 注册系统组
        // 1.输入系统组
        SystemGroup InputSystemGroup = new();
        // 2.逻辑系统组
        SystemGroup LogicSystemGroup = new();
        // 3.输出系统组
        SystemGroup OutputSystemGroup = new();
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
