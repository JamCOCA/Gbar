using System.Collections.Concurrent;

namespace Gbar.Core.Messaging;

public class ConcurrentMessageQueue<T> : IMessageQueue<T>
{
    private readonly ConcurrentQueue<T> _queue = new();

    public void Enqueue(T message) => _queue.Enqueue(message);

    public bool TryDequeue(out T? message) => _queue.TryDequeue(out message);

    public bool IsEmpty => _queue.IsEmpty;
}
