using Gbar.Core.Messaging;
using Gbar.Core.Type.Input;

namespace Gbar.Core.ECS;

/// <summary>
/// 游戏上下文接口，为 ECS 系统组与系统提供访问全局共享服务的入口。
/// </summary>
public interface IGameContext
{
    /// <summary>
    /// 输入消息队列，由 InputThread 生产，ECS 系统组在逻辑帧中消费。
    /// </summary>
    IMessageQueue<InputMessage> InputMessageQueue { get; }
}
