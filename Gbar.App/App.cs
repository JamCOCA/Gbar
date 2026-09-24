using Gbar.App.Logging;
using Gbar.Core;
using Gbar.Core.Components.Celestial;
using Gbar.Core.Components.Identity;
using Gbar.Core.Components.Physics;
using Gbar.Core.Components.Spatial;
using Gbar.Core.Logging;

namespace Gbar.App;

/// <summary>
/// 应用程序入口封装：按 Init -> Debug -> Start -> WaitForQuit -> Quit 的顺序管理服务器生命周期。
/// </summary>
public sealed class App : IDisposable
{
    #region 成员

    private const int NetPort = 9050;
    private const int TimeScale = 100;   // 时间加速：真实轨道周期是小时级，加速 100× 便于观测（可调）

    private GameServer? _server;
    private Task? _statusTask;
    private readonly TaskCompletionSource _quit = new();

    #endregion

    #region 生命周期

    /// <summary>初始化：配置日志、创建服务器并注册系统。</summary>
    public async Task InitAsync()
    {
        Log.AddSink(new LevelFilterSink(new ConsoleSink(), LogLevel.Warn)); // 控制台只显示 Warn 以上
        Log.AddSink(new FileSink("logs"));                                  // 文件保留完整日志
        Log.MinimumLevel = LogLevel.Debug;
        Log.Start();

        _server = new GameServer(tickRate: 20);
        _server.TimeScale = TimeScale;   // 加速模拟（轨道运动是小时级）
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
        var world = _server!.World;
        var registry = _server.ClientRegistry!;

        // 天体：恒星 + 行星 + 卫星（on-rails，位置/自转由 OrbitalSystem 历元解析；Celestial 不进网络快照）
        var star = world.Create(
            new NetworkId { Value = registry.AllocNetId() },
            new GlobalPosition(),
            new Celestial
            {
                Mu = 1.3271244e20,
                SurfaceRadius = 6.9634e8,
                SoiRadius = double.MaxValue,   // 恒星无母天体，覆盖整个星系
                InitialRotation = 0f,
                AngularVelocity = 0.00001f,
            });

        var planet = world.Create(
            new NetworkId { Value = registry.AllocNetId() },
            new GlobalPosition(),
            new Celestial
            {
                Mu = 3.9860044e14,
                SurfaceRadius = 6.371e6,
                SoiRadius = 9.25e8,
                InitialRotation = 0.5f,
                AngularVelocity = 0.0001f,
            },
            new Orbit
            {
                Parent = star,
                SemiMajorAxis = 1.496e11,
                Eccentricity = 0.0167,
                OrbitalPeriod = 1200.0,   // 测试用短周期（20 min），便于观察轨道运动
                InitialPhase = 0f,
            });

        world.Create(
            new NetworkId { Value = registry.AllocNetId() },
            new GlobalPosition(),
            new Celestial
            {
                Mu = 4.282837e13,
                SurfaceRadius = 3.3895e6,
                SoiRadius = 5.77e8,
                InitialRotation = 1.2f,
                AngularVelocity = 0.00009f,
            },
            new Orbit
            {
                Parent = star,
                SemiMajorAxis = 2.279e11,
                Eccentricity = 0.0934,
                OrbitalPeriod = 2000.0,   // 测试用短周期（~33 min）
                InitialPhase = 2.0,
            });

        world.Create(
            new NetworkId { Value = registry.AllocNetId() },
            new GlobalPosition(),
            new Celestial
            {
                Mu = 4.9048695e12,
                SurfaceRadius = 1.7374e6,
                SoiRadius = 6.62e7,
                InitialRotation = 0f,
                AngularVelocity = 0.0001f,
            },
            new Orbit
            {
                Parent = planet,
                SemiMajorAxis = 3.844e8,
                Eccentricity = 0.0549,
                OrbitalPeriod = 240.0,    // 测试用短周期（4 min）
                InitialPhase = 1.0,
            });

        // 引力测试 1：掠过偏转——双曲轨道（v∞≈5000 m/s、撞击参数 1.8e7 m → ~83° 偏转），掠过行星后脱离
        world.Create(
            new NetworkId { Value = registry.AllocNetId() },
            new ParentRef { Parent = planet },
            new LocalPosition { X = -5_000_000_000L, Y = 1_800_000_000L },   // 距行星心 -5e7 m、+1.8e7 m
            new LocalVelocity { X = 2_500_000 },                              // +5000 m/s（v_fix = v_si × 500）
            new Mass { Value = 100_000 },                                     // 1000 kg
            new Impulse());

        // 引力测试 2：引力捕获——椭圆轨道（r=3e7 m、切向 2500 m/s → e≈0.53），绕行星长期公转
        world.Create(
            new NetworkId { Value = registry.AllocNetId() },
            new ParentRef { Parent = planet },
            new LocalPosition { X = 3_000_000_000L },                          // 距行星心 3e7 m
            new LocalVelocity { Y = 1_250_000 },                               // 切向 2500 m/s
            new Mass { Value = 100_000 },
            new Impulse());

        // 相对静止测试：被行星捕获的共动锚点——无速度、无质量，固定在行星参考系中，随行星绕恒星做圆周运动
        world.Create(
            new NetworkId { Value = registry.AllocNetId() },
            new ParentRef { Parent = planet },
            new LocalPosition { Y = 4_000_000_000L });                        // 距行星心 4e7 m，与行星相对静止
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
