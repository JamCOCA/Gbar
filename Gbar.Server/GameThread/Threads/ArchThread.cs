using Arch.Core;
using Gbar.Core.ECS;
using Gbar.Core.ECS.Components;
using Gbar.Core.Type.Input;
using Gbar.Core.Messaging;

namespace Gbar.Server.GameThread.Threads;

public class ArchThread : BaseGameThread, IGameContext
{
    public List<SystemGroup> SystemGroups { get; } = [];
    public IMessageQueue<InputMessage> InputMessageQueue { get; }

    public ArchThread(IMessageQueue<InputMessage> inputMessageQueue)
    {
        InputMessageQueue = inputMessageQueue;
        TicksPerSecond = 60;
    }

    protected override void OnStart()
    {
        EcsWorldManager.Initialize();
        Debug("Arch ECS 线程已启动");
        var world = EcsWorldManager.World;

        // 注册系统组
        SystemGroup inputSystemGroup = new();
        SystemGroup logicSystemGroup = new();
        SystemGroup outputSystemGroup = new();

        // TODO: 向系统组中添加 ISystem

        SystemGroups.Add(inputSystemGroup);
        SystemGroups.Add(logicSystemGroup);
        SystemGroups.Add(outputSystemGroup);

        foreach (var group in SystemGroups)
        {
            group.Start(world, this);
        }
    }

    protected override void OnStop()
    {
        foreach (var group in SystemGroups)
        {
            group.Destroy();
        }
        SystemGroups.Clear();
        EcsWorldManager.Dispose();
        Debug("Arch ECS 线程已停止");
    }

    protected override void Update(float deltaTime)
    {
        foreach (var group in SystemGroups)
        {
            group.Update(deltaTime);
        }
    }
}
