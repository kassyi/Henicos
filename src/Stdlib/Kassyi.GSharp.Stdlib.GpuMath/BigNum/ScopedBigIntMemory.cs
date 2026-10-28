namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides a deterministic, stack-scoped RAII wrapper for temporary device memory allocations.
/// </summary>
/// <param name="handle">
/// The device memory handle bound to this scope. An owning handle is freed by <see cref="Dispose"/> through the manager
/// that allocated it; a borrowed handle is only used.
/// </param>
/// <remarks>
/// <para>
/// Because this type is a <see langword="ref struct"/>, it is strictly stack-allocated and cannot escape the calling frame,
/// preventing accidental memory leaks or heap allocations during high-throughput GPU kernel staging.
/// </para>
/// </remarks>
public ref struct ScopedBigIntMemory(GpuBigIntMemoryHandle handle)
{
    private GpuBigIntMemoryHandle _handle = handle;

    /// <summary>
    /// Gets the underlying device memory handle.
    /// </summary>
    public GpuBigIntMemoryHandle Handle => _handle;

    /// <summary>
    /// Releases an owned allocation back to the <see cref="IGpuMemoryManager"/> that allocated it; a borrowed handle is
    /// left alone.
    /// </summary>
    public void Dispose()
    {
        if (!_handle.IsDefault && _handle.OwnsBuffer && _handle.Buffer.Manager is { } manager)
        {
            manager.Free(_handle);
        }

        _handle = default;
    }

    /// <summary>
    /// Defines an implicit conversion from a scoped allocation to its raw <see cref="GpuBigIntMemoryHandle"/>.
    /// </summary>
    /// <param name="memory">The scoped memory wrapper.</param>
    public static implicit operator GpuBigIntMemoryHandle(in ScopedBigIntMemory memory) => memory.Handle;
}

