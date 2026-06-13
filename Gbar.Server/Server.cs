using Gbar.Core.Type.Input;
using Gbar.Server.GameThread.Threads;
using Gbar.Core.Messaging;

namespace Gbar.Server;

public class Server
{
    public void Run()
    {
        Console.WriteLine("服务器正在启动...");
        // 注册信息队列
        var messageQueue = new ConcurrentMessageQueue<InputMessage>();

        var inputThread = new InputThread(messageQueue);
        var archThread = new ArchThread(messageQueue);
        var monitorThread = new MonitorThread();

        foreach (var thread in BaseGameThread.AllThreads)
        {
            thread.Start();
        }

        BaseGameThread.WaitAll();
        Console.WriteLine("服务器正在关闭...");
    }

    public void StartEcs()
    {
        
    }
}
