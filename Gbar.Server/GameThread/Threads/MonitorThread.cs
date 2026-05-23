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
        MonitorEnabled = false;
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

    protected override void Update()
    {
        if (!MonitorEnabled) return;
        var world = EcsWorldManager.World;
        Debug($"当前实体数量: {world.Size}");


        // var query = new QueryDescription().WithAll<Health,Position,Velocity>();
        // world.Query(in query,(ref Health health, ref Position position, ref Velocity velocity) =>
        // {
        //     Debug($"实体位置: {position}, 速度: {velocity}, 生命值: {health}");
        // });

        // 这里可以添加监视逻辑，例如监控服务器状态、资源使用等
        Debug("监视线程正在运行...");
    }
}