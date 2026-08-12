using Arch.Core;
using Gbar.Core;
using Gbar.Core.Components;

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

Console.WriteLine("服务器已启动（20 tick/s），按回车或 Ctrl+C 停止...");
await Task.WhenAny(quit.Task, Task.Run(Console.ReadLine));

await server.StopAsync();
Console.WriteLine($"共运行 {server.Tick} tick");

server.Dispose();
