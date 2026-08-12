using Arch.Core;
using Gbar.App.Logging;
using Gbar.Core;
using Gbar.Core.Components;
using Gbar.Core.Logging;

Log.AddSink(new ConsoleSink());
Log.AddSink(new FileSink("logs"));
Log.MinimumLevel = LogLevel.Debug;
Log.Start();

var server = new GameServer(tickRate: 20);
server.RegisterSystems();   // 反射扫描所有 FixedSystem/UpdateSystem 子类并自动注册

var random = new Random();
for (var i = 0; i < 100; i++)
{
    server.World.Create(
        new Position { X = 0, Y = 0 },
        new Velocity { X = (float)(random.NextDouble() * 20 - 10), Y = (float)(random.NextDouble() * 20 - 10) });
}

server.Initialize();
server.Start();

var quit = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    quit.TrySetResult();
};

Log.Info("服务器已启动（20 tick/s），按回车或 Ctrl+C 停止...");

var statusTask = Task.Run(async () =>
{
    var lastTick = server.Tick;
    var lastTime = DateTime.UtcNow;
    while (!quit.Task.IsCompleted)
    {
        await Task.Delay(1000);
        var now = DateTime.UtcNow;
        var currentTick = server.Tick;
        var tps = (currentTick - lastTick) / (now - lastTime).TotalSeconds;
        Log.Info($"Tick速率: {tps:F1} tick/s | 总Tick: {currentTick}");
        lastTick = currentTick;
        lastTime = now;
    }
});

await Task.WhenAny(quit.Task, Task.Run(Console.ReadLine));
quit.TrySetResult();
await statusTask;

await server.StopAsync();
Log.Info($"共运行 {server.Tick} tick");

server.Dispose();
Log.Shutdown();
