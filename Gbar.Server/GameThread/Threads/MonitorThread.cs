using Arch.Core;
using Gbar.Core.ECS;
using Gbar.Core.ECS.Components;
using Microsoft.VisualBasic;

namespace Gbar.Server;
public class MonitorThread : BaseGameThread
{
    bool MonitorEnabled;
    public MonitorThread()
    {
        MonitorEnabled = true;
        TicksPerSecond = 1; // 每秒更新一次
    }

    protected override void OnStart()
    {
        Debug("监视线程已启动");
    }

    protected override void OnStop()
    {
        Debug("监视线程已停止");
        
    }

    protected override void Update(float deltaTime)
    {
        if (!MonitorEnabled) return;
        var world = EcsWorldManager.World;
        Debug($"当前实体数量: {world.Size}");
        Debug($"tickers: {TicksPerSecond}, deltaTime: {deltaTime}");
        Debug("监视线程正在运行...");
    }
}