namespace Gbar.Core.Messaging;

public interface IMessageQueue<T>
{
    void Enqueue(T message);
    bool TryDequeue(out T? message);
    bool IsEmpty { get; }
}
