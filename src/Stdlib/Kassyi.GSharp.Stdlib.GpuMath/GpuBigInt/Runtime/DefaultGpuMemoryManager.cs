using System.Collections.Concurrent;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt.Runtime;

namespace Kassyi.GSharp.Stdlib.GpuMath.GpuBigInt.Runtime;

internal sealed class DefaultGpuMemoryManager(IGpuExecutionProvider provider, IntPtr stream)
    : IGpuMemoryManager, IDisposable
{
    private readonly ConcurrentDictionary<int, Lazy<ConcurrentBag<GpuBigIntMemoryHandle>>> _tempPool = new();
    private readonly ConcurrentDictionary<IntPtr, byte> _activeAllocations = new();
    private int _disposed;

    public GpuBigIntMemoryHandle Allocate(int numWords)
    {
        if (_tempPool.TryGetValue(numWords, out var lazyBag) && lazyBag.IsValueCreated &&
            lazyBag.Value.TryTake(out var temp))
        {
            _activeAllocations.TryAdd(temp.Buffer.Pointer, 0);
            return temp;
        }

        var ptr = provider.AllocateMemory(numWords);
        _activeAllocations.TryAdd(ptr, 0);
        provider.ClearMemoryAsync(ptr, numWords, stream);
        var buffer = new GpuBigIntDeviceBuffer(ptr, numWords);
        return new GpuBigIntMemoryHandle(buffer, 0, numWords);
    }

    public ScopedBigIntMemory AllocateTemp(int numWords)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        var handle = Allocate(numWords);
        return new ScopedBigIntMemory(this, handle);
    }

    public void Free(GpuBigIntMemoryHandle handle)
    {
        if (handle.IsDefault)
        {
            return;
        }

        // Prevent double frees which cause InvalidValue and Access Violation on CUDA
        if (handle.Buffer.Pointer != IntPtr.Zero && !_activeAllocations.TryRemove(handle.Buffer.Pointer, out _))
        {
            return;
        }

        if (Volatile.Read(ref _disposed) == 1)
        {
            // Already disposed, actually free it
            if (handle.Buffer.Pointer == IntPtr.Zero)
            {
                return;
            }

            provider.FreeMemory(handle.Buffer.Pointer);
            handle.Buffer.Dispose();
            return;
        }

        if (handle.Buffer.Pointer != IntPtr.Zero)
        {
            provider.ClearMemoryAsync(handle.Buffer.Pointer, handle.Buffer.TotalWords, stream);
        }

        // Return to pool based on total size
        var bag = _tempPool.GetOrAdd(handle.Buffer.TotalWords,
            _ => new Lazy<ConcurrentBag<GpuBigIntMemoryHandle>>(() => [])).Value;
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
                if (handle.Buffer.Pointer == IntPtr.Zero)
                {
                    continue;
                }

                provider.FreeMemory(handle.Buffer.Pointer);
                handle.Buffer.Dispose();
            }
        }

        _tempPool.Clear();

        foreach (var ptr in _activeAllocations.Keys)
        {
            if (ptr == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                provider.FreeMemory(ptr);
            }
            catch
            {
                // Best-effort during disposal
            }
        }

        _activeAllocations.Clear();
    }
}
