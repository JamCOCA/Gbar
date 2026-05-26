using System.Diagnostics;

namespace Gbar.Server;

public abstract class BaseGameThread
{
    private Thread? _thread;
    private bool _running;
    private readonly Stopwatch _timer = new();
    private static readonly List<BaseGameThread> _allThreads = new();
    public static IReadOnlyList<BaseGameThread> AllThreads => _allThreads.AsReadOnly();

    public int TicksPerSecond { get; set; } = 20;
    public bool IsRunning => _running;
    public static bool DebugEnabled { get; set; } = true;

    protected BaseGameThread()
    {
        _allThreads.Add(this);
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _timer.Restart();
        OnStart();
        _thread = new Thread(RunLoop) { IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer.Stop();
        OnStop();
    }

    public void Join() => _thread?.Join();

    public static void WaitAll()
    {
        foreach (var t in _allThreads)
            t.Join();
    }

    // 默认的循环方法，子类可以重写它来实现不同的行为
    protected virtual void RunLoop()
    {
        long nextTick = _timer.ElapsedMilliseconds;
        long interval = 1000 / TicksPerSecond;
        double lastTime = _timer.Elapsed.TotalSeconds;

        while (_running)
        {
            long now = _timer.ElapsedMilliseconds;
            if (now >= nextTick)
            {
                nextTick += interval;
                double currentTime = _timer.Elapsed.TotalSeconds;
                float deltaTime = (float)(currentTime - lastTime);
                lastTime = currentTime;
                Update(deltaTime);
            }
            Thread.Sleep(1);
        }
    }

    protected void Debug(string message)
    {
        if (!DebugEnabled) return;
        var time = DateTime.Now.ToString("HH:mm:ss.fff");
        var name = GetType().Name;
        Console.WriteLine($"[{time}] [{name}] {message}");
    }

    protected virtual void Update(float deltaTime) { }
    protected virtual void OnStart() { }
    protected virtual void OnStop() { }
}

