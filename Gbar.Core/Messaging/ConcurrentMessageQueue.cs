using System;
using System.Collections.Generic;
using System.Threading;

namespace Gbar.Core.Messaging;

public class ConcurrentMessageQueue<T> : IMessageQueue<T>
{
    private readonly T[] _a;
    private readonly T[] _b;
    private T[] _write;
    private T[] _read;
    private int _writeCount;
    private int _readCount;
    private int _readPos;

    public ConcurrentMessageQueue(int capacity = 100000)
    {
        _a = new T[capacity];
        _b = new T[capacity];
        _write = _a;
        _read = _b;
    }

    public void Enqueue(T message)
    {
        int i = Interlocked.Increment(ref _writeCount) - 1;
        _write[i] = message;
    }

    public bool TryDequeue(out T? message)
    {
        if (_readPos < _readCount)
        {
            message = _read[_readPos++];
            return true;
        }
        Swap();
        if (_readPos < _readCount)
        {
            message = _read[_readPos++];
            return true;
        }
        message = default;
        return false;
    }

    public void DequeueAll(List<T> destination)
    {
        destination.Clear();
        Swap();
        for (int i = 0; i < _readCount; i++)
            destination.Add(_read[i]);
        _readPos = _readCount;
    }

    public bool IsEmpty => _readPos >= _readCount && _writeCount == 0;

    private void Swap()
    {
        (_write, _read) = (_read, _write);
        _readCount = Interlocked.Exchange(ref _writeCount, 0);
        _readPos = 0;
    }
}
