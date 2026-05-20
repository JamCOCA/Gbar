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

    // 默认的循环方法，子类可以重写它来实现不同的行为
    protected virtual void RunLoop()
    {
        long nextTick = _timer.ElapsedMilliseconds;
        long interval = 1000 / TicksPerSecond;

        while (_running)
        {
            long now = _timer.ElapsedMilliseconds;
            if (now >= nextTick)
            {
                nextTick += interval;
                Update();
            }
            Thread.Sleep(1);
        }
    }

    protected virtual void Update() { }
    protected virtual void OnStart() { }
    protected virtual void OnStop() { }
}

