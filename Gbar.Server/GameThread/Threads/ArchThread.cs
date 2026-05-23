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

        SpawnTestEntities();
    }

    protected override void OnStop()
    {
        EcsWorldManager.Dispose();
        Debug("Arch ECS 线程已停止");
    }

    protected override void Update()
    {
        var world = EcsWorldManager.World;

        var moveQuery = new QueryDescription().WithAll<Position, Velocity>();
        world.Query(in moveQuery, (Entity entity, ref Position pos, ref Velocity vel) =>
        {
            pos.X += vel.Dx;
            pos.Y += vel.Dy;
            pos.Z += vel.Dz;
        });
    }

    private void SpawnTestEntities()
    {
        var world = EcsWorldManager.World;

        for (int i = 0; i < 5; i++)
        {
            world.Create(
                new Position(i * 10, 0, 0),
                new Velocity(1, 0, 0),
                new Health(100, 100),
                new Mana(100, 100),
                new State(EntityState.Idle)
            );
        }

        Debug("已生成 5 个测试实体");
    }
}
