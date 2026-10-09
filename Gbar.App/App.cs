using Gbar.App.Logging;
using Gbar.Core;
using Gbar.Core.Components.Physics;
using Gbar.Net.Protocol.Components;
using Gbar.Core.Logging;
using Gbar.Core.Persistence.Database;
using Gbar.Core.Persistence.Users;
using Gbar.Core.Service;
using Gbar.Net.Protocol.Entities;
using Gbar.Physics;
using Microsoft.Extensions.Configuration;

namespace Gbar.App;

/// <summary>
/// 应用程序入口封装：按 Init -> Debug -> Start -> WaitForQuit -> Quit 的顺序管理服务器生命周期。
/// </summary>
public sealed class App : IDisposable
{
    #region 成员

    private const int NetPort = 9050;

    private GameServer? _server;
    private Task? _statusTask;
    private readonly TaskCompletionSource _quit = new();

    private GbarDbContextFactory? _dbFactory;
    private UserRepository? _users;

    /// <summary>用户账号仓储（注册 / 登录用），由 <see cref="InitAsync"/> 按配置构建。</summary>
    public UserRepository? Users => _users;

    #endregion

    #region 生命周期

    /// <summary>初始化：配置日志、创建服务器并注册系统。</summary>
    public async Task InitAsync()
    {
        Log.AddSink(new LevelFilterSink(new ConsoleSink(), LogLevel.Warn)); // 控制台只显示 Warn 以上
        Log.AddSink(new FileSink("logs"));                                  // 文件保留完整日志
        Log.MinimumLevel = LogLevel.Debug;
        Log.Start();

        // 读取配置（appsettings.json），构建数据库工厂与用户仓储
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var connectionString = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("appsettings.json 缺少 ConnectionStrings:Postgres");

        _dbFactory = new GbarDbContextFactory(connectionString);
        _dbFactory.EnsureCreated();   // 数据库/表不存在时自动创建
        _users = new UserRepository(_dbFactory);

        _server = new GameServer(tickRate: 20);
        _server.Auth = new AuthService(_users);
        _server.RegisterSystems();  // 反射扫描所有 FixedSystem/UpdateSystem 子类并自动注册
        _server.StartNet(NetPort);  // 启动网络监听（NetServerSystem/NetSyncSystem 随反射注册生效）

        _server.Initialize();
    }

    /// <summary>
    /// 调试注入点：主循环启动前调用，此时直接写 World 是线程安全的。
    /// 在这里手动注入测试实体。
    /// </summary>
    public void Debug()
    {
        var factory = _server!.EntityFactory!;

        // 占位：游荡敏化人（Kinematic 刚体 + 移动意图，测试物理驱动移动与增量同步）
        factory.CreateSurfaceWanderer(
            new Position { X = 10.0, Y = 0.95, Z = 20.0 },
            new MoveIntent { DirX = 0.88f, DirZ = 0.47f, Speed = 1.7f });   // ≈ 原 (1.5, 0.8) 的速度

        factory.CreateSurfaceWanderer(
            new Position { X = -40.0, Y = 0.95, Z = 60.0 },
            new MoveIntent { DirX = 0.0f, DirZ = -1.0f, Speed = 2.0f });

        // 占位：静态资源点（无速度，测试类型区分）
        factory.CreateStaticEntity(EntityType.CpcSpring,
            new Position { X = 200.0, Y = -5.0, Z = 150.0 });

        factory.CreateStaticEntity(EntityType.ReactorRuin,
            new Position { X = -300.0, Y = -120.0, Z = 80.0 });

        // 物理演示：动态盒/球从空中落到地面（重力 + 碰撞 + 位姿回写同步）
        factory.CreatePhysicsBody(EntityType.Item,
            new Position { X = 0.0, Y = 8.0, Z = 0.0 },
            PhysicsShape.Box(0.5f, 0.5f, 0.5f), 1f);

        factory.CreatePhysicsBody(EntityType.Item,
            new Position { X = 1.5, Y = 12.0, Z = 1.0 },
            PhysicsShape.Sphere(0.6f), 1f);

        // 物理演示：地面之上的一块静态平台，供动态体撞击/停靠
        factory.CreateStaticCollider(EntityType.Structure,
            new Position { X = 4.0, Y = 3.0, Z = 0.0 },
            PhysicsShape.Box(2f, 0.25f, 2f));
    }

    /// <summary>启动：开启主循环、注册退出信号并启动状态汇报任务。</summary>
    public void Start()
    {
        var server = _server ?? throw new InvalidOperationException("请先调用 InitAsync。");

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _quit.TrySetResult();
        };

        server.Start();
        Log.Info($"服务器已启动（{server.TickRate} tick/s，监听 {NetPort}），按回车或 Ctrl+C 停止...");

        _statusTask = Task.Run(StatusLoop);
    }

    /// <summary>等待退出信号（回车或 Ctrl+C）。</summary>
    public async Task WaitForQuitAsync()
    {
        await Task.WhenAny(_quit.Task, Task.Run(Console.ReadLine));
        _quit.TrySetResult();   // 回车退出时也通知状态任务结束
        if (_statusTask is not null) await _statusTask;
    }

    /// <summary>退出：停止主循环、释放服务器并关闭日志。</summary>
    public async Task QuitAsync()
    {
        if (_server is null) return;

        await _server.StopAsync();
        Log.Info($"共运行 {_server.Tick} tick");

        _server.Dispose();
        _server = null;
        Log.Shutdown();
    }

    public void Dispose() => _server?.Dispose();

    #endregion

    /// <summary>每秒汇报一次实际 Tick 速率。</summary>
    private async Task StatusLoop()
    {
        var lastTick = _server!.Tick;
        var lastTime = DateTime.UtcNow;
        while (!_quit.Task.IsCompleted)
        {
            await Task.Delay(1000);
            var now = DateTime.UtcNow;
            var currentTick = _server.Tick;
            var tps = (currentTick - lastTick) / (now - lastTime).TotalSeconds;
            Log.Info($"Tick速率: {tps:F1} tick/s | 总Tick: {currentTick}");
            lastTick = currentTick;
            lastTime = now;
        }
    }
}
