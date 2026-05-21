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

    protected override void Update()
    {
        if (!MonitorEnabled) return;
        var world = EcsWorldManager.World;
        Debug($"当前实体数量: {world.Size}");


        var query = new QueryDescription().WithAll<Health,Position,Velocity>();
        var entitylist = new Entity[100];
        world.GetEntities(in query,entitylist,0);
        foreach (var entity in entitylist)
        {
            if (entity == Entity.Null) continue;
            var health = world.Get<Health>(entity);
            var position = world.Get<Position>(entity);
            var velocity = world.Get<Velocity>(entity);
            Debug($"实体 {entity}: 生命值={health.Current}");
        }
        // 这里可以添加监视逻辑，例如监控服务器状态、资源使用等
        Debug("监视线程正在运行...");
    }
}