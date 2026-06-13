namespace Gbar.Core.Messaging;

/// <summary>
/// 线程安全消息队列接口，用于跨线程生产与消费消息。
/// </summary>
/// <typeparam name="T">消息类型。</typeparam>
public interface IMessageQueue<T>
{
    /// <summary>
    /// 将一条消息加入队列。由生产者线程调用。
    /// </summary>
    /// <param name="message">待入队的消息。</param>
    void Enqueue(T message);

    /// <summary>
    /// 尝试从队列中取出一条消息。
    /// </summary>
    /// <param name="message">取出的消息；若无消息则为默认值。</param>
    /// <returns>是否成功取出消息。</returns>
    bool TryDequeue(out T? message);

    /// <summary>
    /// 将当前可读缓冲区的所有消息批量取出并写入目标列表。
    /// 推荐在 ECS 逻辑帧中使用，以减少逐条出队开销。
    /// </summary>
    /// <param name="destination">接收消息的列表，会先被清空。</param>
    void DequeueAll(List<T> destination);

    /// <summary>
    /// 判断队列当前是否为空。
    /// </summary>
    bool IsEmpty { get; }
}
