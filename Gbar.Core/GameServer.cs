using System.Linq.Expressions;
using Arch.Core;
using Arch.System;
using Gbar.Core.Net;
using Schedulers;

namespace Gbar.Core;

/// <summary>
/// 游戏服务器核心：持有 ECS 世界，以固定频率驱动系统更新。
/// 系统通过 <see cref="Systems.FixedSystem"/>/<see cref="Systems.UpdateSystem"/> 基类构造时自动注册，
/// 再 <see cref="Initialize"/>，最后 <see cref="Start"/> 启动主循环。
/// </summary>
public sealed class GameServer : IDisposable
{
    # region 成员
    private readonly World _world;
    private readonly UpdateSystemGroup _updateSystems;        // 变步长更新（delta 为实际流逝时间）
    private readonly FixedUpdateSystemGroup _fixedSystems;    // 定步长更新（delta 固定为 1/TickRate）
    private CancellationTokenSource? _cts;          // 控制主循环的取消令牌
    private Task? _loopTask;                        // 后台主循环任务

    /// <summary>ECS 世界，所有实体与组件都存放在这里。</summary>
    public World World => _world;

    /// <summary>每秒逻辑帧数。</summary>
    public int TickRate { get; }

    /// <summary>
    /// 周期纠偏 Keyframe 的发送间隔（tick），默认 10 秒（10 × <see cref="TickRate"/>）。
    /// 不同客户端按 ClientId 相位错开（见 NetSyncSystem）；≤0 表示关闭周期纠偏。
    /// </summary>
    public int KeyframeIntervalTicks { get; set; }

    /// <summary>时间加速倍率：每帧跑 TimeScale 个固定步进（&gt;1 加速模拟，供观测小时级的真实轨道运动）。</summary>
    public int TimeScale { get; set; } = 1;

    /// <summary>已执行的固定步进数（TimeScale &gt; 1 时与真实帧数不成 1:1），启动后从 0 递增。</summary>
    public long Tick { get; private set; }

    /// <summary>主循环是否正在运行。</summary>
    public bool IsRunning => _cts is not null;

    /// <summary>update 组（变步长），持有本组的命令缓冲 Hub。</summary>
    public UpdateSystemGroup UpdateSystems => _updateSystems;

    /// <summary>fixed 组（定步长），持有本组的命令缓冲 Hub。</summary>
    public FixedUpdateSystemGroup FixedSystems => _fixedSystems;

    /// <summary>网络服务器（LiteNetLib 封装）。经 <see cref="StartNet"/> 创建并启动后非空。</summary>
    public NetServer? NetServer { get; private set; }

    /// <summary>连接注册表 + NetId 分配器，随 <see cref="StartNet"/> 创建。</summary>
    public ClientRegistry? ClientRegistry { get; private set; }

    /// <summary>每帧触发，参数为实际流逝时间（秒）。</summary>
    public event Action<float>? OnUpdate;

    /// <summary>每帧触发，参数为固定步长（1/TickRate 秒），适合物理/逻辑模拟。</summary>
    public event Action<float>? OnFixedUpdate;

    #endregion

    // 构造函数
    public GameServer(int tickRate = 20)
    {
        TickRate = tickRate;
        KeyframeIntervalTicks = tickRate * 10;
        _world = World.Create();
        _updateSystems = new UpdateSystemGroup();
        _fixedSystems = new FixedUpdateSystemGroup();
    }

    /// <summary>初始化所有已注册系统，必须在 <see cref="Start"/> 之前调用。</summary>
    public void Initialize()
    {
        _updateSystems.Initialize();
        _fixedSystems.Initialize();
    }

    /// <summary>
    /// 反射扫描程序集，自动实例化并注册所有 <see cref="Systems.FixedSystem"/>/<see cref="Systems.UpdateSystem"/> 子类。
    /// 默认扫描调用方程序集与 Gbar.Core；执行顺序由 <see cref="Systems.SystemOrderAttribute"/> 决定，未标注按类型名排序。
    /// 系统构造函数必须是 <c>(GameServer)</c> 单参数。
    /// </summary>
    public void RegisterSystems(params System.Reflection.Assembly[] assemblies)
    {
        if (assemblies.Length == 0)
            assemblies = new[] { System.Reflection.Assembly.GetCallingAssembly(), typeof(GameServer).Assembly };

        var systemTypes = assemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface
                && (typeof(Systems.FixedSystem).IsAssignableFrom(t) || typeof(Systems.UpdateSystem).IsAssignableFrom(t)))
            .OrderBy(t => t.GetCustomAttributes(typeof(Systems.SystemOrderAttribute), false)
                is [Systems.SystemOrderAttribute attr] ? attr.Order : 0)
            .ThenBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in systemTypes)
            Activator.CreateInstance(type, this);   // 构造函数内自动注册进对应组
    }

    /// <summary>
    /// 创建并启动网络服务器，监听指定端口。重复调用无效。
    /// 通常在 <see cref="Start"/> 之前调用；网络系统（NetServerSystem/NetSyncSystem）会自动读取本实例。
    /// </summary>
    public void StartNet(int port, int maxConnections = 32)
    {
        if (NetServer is not null) return;
        NetServer = new NetServer();
        ClientRegistry = new ClientRegistry();
        NetServer.Start(port, maxConnections);
    }

    /// <summary>启动后台主循环。重复调用无效。</summary>
    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => Loop(_cts.Token));
    }

    /// <summary>停止主循环并等待其退出，释放令牌资源。</summary>
    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        if (_loopTask is not null) await _loopTask; // 等待循环真正结束
        _cts.Dispose();
        _cts = null;
        _loopTask = null;

        // 循环已退出：断开所有网络连接（无新数据写入）
        NetServer?.Stop();
    }

    /// <summary>
    /// 主循环：以 1/TickRate 间隔逐帧推进两组系统，帧末统一回放结构变更。
    /// JobScheduler 的创建/调度/销毁都在本线程（Arch 的 ParallelQuery 依赖它，且只能在创建线程调用）。
    /// </summary>
    private void Loop(CancellationToken ct)
    {
        // Arch 的 ParallelQuery 必需，无默认实例；不能移到构造函数（Schedule 只允许从创建线程调用）
        var jobScheduler = new JobScheduler(new JobScheduler.Config
        {
            ThreadPrefixName = "Gbar",
            ThreadCount = 0,                // 0 = 按 CPU 核数自动决定
            MaxExpectedConcurrentJobs = 64,
            StrictAllocationMode = false,
        });
        World.SharedJobScheduler = jobScheduler;
        var interval = TimeSpan.FromSeconds(1.0 / TickRate);
        var timer = new PeriodicTimer(interval);    // 固定节拍定时器
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var last = stopwatch.Elapsed;

        var fixedDelta = (float)interval.TotalSeconds;  // 恒定步长，保证模拟确定性
        var accumulator = 0f;                           // 累积真实流逝时间，驱动 fixed 补帧
        var maxFixedSteps = Math.Max(5, TimeScale * 4); // 单帧最大步进数（时间加速时放大），防止死亡螺旋

        while (true)
        {
            bool hasNext;
            try
            {
                // 同步阻塞等待下一拍，线程不释放，保证始终在同一线程上运行
                hasNext = timer.WaitForNextTickAsync(ct).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                break;  // StopAsync 取消令牌，退出循环
            }
            if (!hasNext) break;

            var now = stopwatch.Elapsed;
            var delta = (float)(now - last).TotalSeconds; // 实际流逝时间，作为变步长 delta
            last = now;

            // 变步长更新：每帧恰好一次，事件先于系统触发
            OnUpdate?.Invoke(delta);
            _updateSystems.BeforeUpdate(delta);
            _updateSystems.Update(delta);
            _updateSystems.AfterUpdate(delta);
            _updateSystems.Commands.Playback(_world);   // 统一回放本组缓存的结构变更

            // 定步长更新：按累积时间跑 0~N 次（时间加速按 TimeScale 放大）。
            // Tick 每个固定步进递增，保证 Tick 与物理步进一一对应（轨道/网络同步都按 Tick 对齐）。
            accumulator += delta * TimeScale;
            var steps = 0;
            while (accumulator >= fixedDelta && steps < maxFixedSteps)
            {
                Tick++;
                OnFixedUpdate?.Invoke(fixedDelta);
                _fixedSystems.BeforeUpdate(fixedDelta);
                _fixedSystems.Update(fixedDelta);
                _fixedSystems.AfterUpdate(fixedDelta);
                _fixedSystems.Commands.Playback(_world);    // 每次固定步进后立即回放

                accumulator -= fixedDelta;
                steps++;
            }
            if (steps == maxFixedSteps) accumulator = 0f;   // 补不过来就丢弃积压，避免越滚越大
        }

        jobScheduler.Dispose(); // 循环退出后回收，worker 线程全部退出
    }

    /// <summary>
    /// 释放资源：取消循环、销毁系统组与 ECS 世界。
    /// 注意：仅触发取消，不等待循环退出；需要确保循环已结束请先调用 <see cref="StopAsync"/>。
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        NetServer?.Dispose();
        _updateSystems.Dispose();   // 组 Dispose 会连带释放其 Commands
        _fixedSystems.Dispose();
        World.Destroy(_world);
    }
}
