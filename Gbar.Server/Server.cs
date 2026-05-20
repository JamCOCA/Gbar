using Gbar.Server.GameThread.Threads;

namespace Gbar.Server;

public class Server : BaseGameThread
{
    public Server()
    {
        AddSubThread(new InputThread());
    }

    protected override void Update()
    {
    }

    public new void Run()
    {
        Console.WriteLine("服务器正在启动...");
        Start();
        Join();
        Console.WriteLine("服务器已停止");
    }
}
