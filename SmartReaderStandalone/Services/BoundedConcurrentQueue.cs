using System.Collections.Concurrent;
using System.Threading;

public class BoundedConcurrentQueue<T>
{
    private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
    private readonly int _limit;
    private int _count; // Atomic counter

    public BoundedConcurrentQueue(int limit)
    {
        _limit = limit;
        _count = 0;
    }

    public int Count => _count;

    public bool TryEnqueue(T item)
    {
        if (Interlocked.Increment(ref _count) > _limit)
        {
            Interlocked.Decrement(ref _count);
            return false; // Queue is full
        }

        _queue.Enqueue(item);
        return true;
    }

    public bool TryDequeue(out T item)
    {
        if (_queue.TryDequeue(out item))
        {
            Interlocked.Decrement(ref _count);
            return true;
        }
        return false;
    }

    public void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _count, 0);
    }

    public T[] ToArray()
    {
        return _queue.ToArray();
    }
}
