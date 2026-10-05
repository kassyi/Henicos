using System.Collections.Concurrent;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;

/// <summary>
/// Provides a pooled device memory manager implementation for GPU large-integer operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Frequent small device allocations (<c>cuMemAlloc</c>) introduce significant driver overhead and host-device synchronization stalls.
/// <see cref="DefaultGpuMemoryManager"/> maintains size-segregated concurrent pools of reusable device buffers to minimize allocation latency.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Thread-safe across concurrent GPU stream staging and disposal operations.
/// </para>
/// </remarks>
internal sealed class DefaultGpuMemoryManager(IGpuExecutionProvider provider, IntPtr stream)
    : IGpuMemoryManager, IDisposable
{
    private readonly ConcurrentDictionary<int, Lazy<ConcurrentBag<GpuBigIntMemoryHandle>>> _tempPool = new();
    private readonly ConcurrentDictionary<IntPtr, byte> _activeAllocations = new();
    private int _disposed;

    /// <summary>
    /// Allocates or reuses a device memory handle for the specified word count.
    /// </summary>
    /// <param name="numWords">The number of 32-bit limbs to allocate.</param>
    /// <returns>A <see cref="GpuBigIntMemoryHandle"/> referencing the device buffer.</returns>
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

    /// <summary>
    /// Allocates a temporary, scope-bound device memory handle.
    /// </summary>
    /// <param name="numWords">The required word count.</param>
    /// <returns>A <see cref="ScopedBigIntMemory"/> managing the scoped allocation.</returns>
    public ScopedBigIntMemory AllocateTemp(int numWords)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        var handle = Allocate(numWords);
        return new ScopedBigIntMemory(this, handle);
    }

    /// <summary>
    /// Recycles the specified memory handle back into the internal pool or releases it if disposed.
    /// </summary>
    /// <param name="handle">The memory handle to free or recycle.</param>
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

    /// <summary>
    /// Releases all pooled device buffers and unmanaged resources.
    /// </summary>
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

