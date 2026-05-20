namespace Gbar.Server.GameThread.Threads;
public class InputThread : BaseGameThread
{
    protected override void OnStart()
    {
        Debug("输入线程已启动");
    }
    // 重写循环方法，直接在这里读取输入
    protected override void RunLoop()
    {

        while (IsRunning)
        {
            var line = Console.ReadLine();
            if (line == "stop") Stop();
        }
    }
}