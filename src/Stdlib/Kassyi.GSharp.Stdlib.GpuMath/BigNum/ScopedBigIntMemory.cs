namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides a deterministic, stack-scoped RAII wrapper for temporary device memory allocations.
/// </summary>
/// <param name="manager">The memory manager responsible for recycling or releasing this memory segment.</param>
/// <param name="handle">The device memory handle bound to this scoped allocation.</param>
/// <remarks>
/// <para>
/// Because this type is a <see langword="ref struct"/>, it is strictly stack-allocated and cannot escape the calling frame,
/// preventing accidental memory leaks or heap allocations during high-throughput GPU kernel staging.
/// </para>
/// </remarks>
public ref struct ScopedBigIntMemory(IGpuMemoryManager? manager, GpuBigIntMemoryHandle handle)
{
    private IGpuMemoryManager? _manager = manager;
    private GpuBigIntMemoryHandle _handle = handle;

    /// <summary>
    /// Gets the underlying device memory handle.
    /// </summary>
    public GpuBigIntMemoryHandle Handle => _handle;

    /// <summary>
    /// Releases the temporary allocation back to the associated <see cref="IGpuMemoryManager"/>.
    /// </summary>
    public void Dispose()
    {
        if (_manager != null && !_handle.IsDefault)
        {
            _manager.Free(_handle);
            _manager = null;
            _handle = default;
        }
    }

    /// <summary>
    /// Defines an implicit conversion from a scoped allocation to its raw <see cref="GpuBigIntMemoryHandle"/>.
    /// </summary>
    /// <param name="memory">The scoped memory wrapper.</param>
    public static implicit operator GpuBigIntMemoryHandle(in ScopedBigIntMemory memory) => memory.Handle;
}

