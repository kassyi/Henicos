namespace Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

using System;
using System.Collections.Concurrent;
using Providers;

internal sealed class DefaultGpuMemoryManager(IGpuExecutionProvider provider, IntPtr stream) : IGpuMemoryManager, IDisposable
{
    private readonly ConcurrentDictionary<int, Lazy<ConcurrentBag<BigIntMemoryHandle>>> _tempPool = new();
    private int _disposed;

    public BigIntMemoryHandle Allocate(int numWords)
    {
        var ptr = provider.AllocateMemory(numWords);
        provider.ClearMemoryAsync(ptr, numWords, stream);
        var buffer = new BigIntDeviceBuffer(ptr, numWords);
        return new BigIntMemoryHandle(buffer, 0, numWords);
    }

    public ScopedBigIntMemory AllocateTemp(int numWords)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        if (_tempPool.TryGetValue(numWords, out var lazyBag) && lazyBag.IsValueCreated &&
            lazyBag.Value.TryTake(out var temp))
        {
            return new ScopedBigIntMemory(this, temp);
        }

        // Delegate to standard allocation if not in pool
        var handle = Allocate(numWords);
        return new ScopedBigIntMemory(this, handle);
    }

    public void Free(BigIntMemoryHandle handle)
    {
        if (handle.IsDefault)
        {
            return;
        }

        if (Volatile.Read(ref _disposed) == 1)
        {
            // Already disposed, actually free it
            if (handle.Buffer.Pointer != IntPtr.Zero)
            {
                provider.FreeMemory(handle.Buffer.Pointer);
                handle.Buffer.Dispose();
            }
            return;
        }

        if (handle.Buffer.Pointer != IntPtr.Zero)
        {
            provider.ClearMemoryAsync(handle.Buffer.Pointer, handle.Buffer.TotalWords, stream);
        }

        // Return to pool based on total size
        var bag = _tempPool.GetOrAdd(handle.Buffer.TotalWords, _ => new Lazy<ConcurrentBag<BigIntMemoryHandle>>(() => [])).Value;
        bag.Add(handle);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        foreach (var lazyBag in _tempPool.Values)
        {
            if (!lazyBag.IsValueCreated)
            {
                continue;
            }

            while (lazyBag.Value.TryTake(out var handle))
            {
                if (handle.Buffer.Pointer != IntPtr.Zero)
                {
                    provider.FreeMemory(handle.Buffer.Pointer);
                    handle.Buffer.Dispose();
                }
            }
        }
        _tempPool.Clear();
    }
}
