using Gbar.App.Logging;
using Gbar.Core;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Logging;
using Gbar.Core.Persistence;

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

    #endregion

    #region 生命周期

    /// <summary>初始化：配置日志、创建服务器、注册系统并从持久化存储恢复实体。</summary>
    public async Task InitAsync()
    {
        Log.AddSink(new LevelFilterSink(new ConsoleSink(), LogLevel.Warn)); // 控制台只显示 Warn 以上
        Log.AddSink(new FileSink("logs"));                                  // 文件保留完整日志
        Log.MinimumLevel = LogLevel.Debug;
        Log.Start();

        _server = new GameServer(tickRate: 20);
        _server.Persistence = new JsonPersistenceProvider(
            Path.Combine(AppContext.BaseDirectory, "save", "world.json"));
        _server.RegisterSystems();  // 反射扫描所有 FixedSystem/UpdateSystem 子类并自动注册
        _server.StartNet(NetPort);  // 启动网络监听（NetServerSystem/NetSyncSystem 随反射注册生效）

        _server.Initialize();
        await _server.LoadAsync();  // 启动前从持久化存储恢复实体
    }

    /// <summary>
    /// 调试注入点：主循环启动前调用，此时直接写 World 是线程安全的。
    /// 在这里手动注入测试实体。
    /// </summary>
    public void Debug()
    {
        // 网络同步测试：注入 100 个随机位置的全局域实体，统一沿 +X 方向固定速度运动
        var world = _server!.World;
        var registry = _server.ClientRegistry!;
        for (var i = 0; i < 100; i++)
        {
            world.Create(
                new NetworkId { Value = registry.AllocNetId() },
                new GlobalPosition
                {
                    X = Random.Shared.NextInt64(-100000, 100000),
                    Y = Random.Shared.NextInt64(-100000, 100000),
                    Z = Random.Shared.NextInt64(-100000, 100000),
                },
                new GlobalVelocity { X = 1000, Y = 0, Z = 0 });
        }
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

    /// <summary>退出：停止主循环（自动持久化）、释放服务器并关闭日志。</summary>
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
