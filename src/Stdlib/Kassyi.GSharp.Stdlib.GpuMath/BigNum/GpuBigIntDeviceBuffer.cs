namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Encapsulates a contiguous block of allocated device memory words for GPU arithmetic operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Serves as an internal tracking token and lifetime container for unmanaged GPU memory pointers.
/// Deallocation is handled cooperatively with the parent memory manager or pool.
/// </para>
/// <para>
/// The memory manager reuses the same object for every lease of the buffer. Each lease has a generation: a handle
/// records the generation of the lease it was created for, and freeing the buffer advances the generation, so a copy of
/// a handle from an earlier lease is stale (the same scheme as the token of <c>ManualResetValueTaskSourceCore</c>).
/// </para>
/// </remarks>
/// <param name="pointer">The raw device pointer.</param>
/// <param name="totalWords">The capacity of the buffer in 32-bit words.</param>
/// <param name="manager">
/// The manager that allocated the buffer, or <see langword="null"/> for memory that belongs to someone outside GpuMath
/// and is never freed or reused here.
/// </param>
internal sealed class GpuBigIntDeviceBuffer(IntPtr pointer, int totalWords, IGpuMemoryManager? manager)
{
    private int _generation;

    /// <summary>
    /// Gets the raw unmanaged device memory pointer.
    /// </summary>
    public IntPtr Pointer { get; } = pointer;

    /// <summary>
    /// Gets the total capacity of the device buffer in 32-bit words (limbs).
    /// </summary>
    public int TotalWords { get; } = totalWords;

    /// <summary>
    /// Gets the manager that allocated the buffer, or <see langword="null"/> for external memory.
    /// </summary>
    internal IGpuMemoryManager? Manager { get; } = manager;

    /// <summary>
    /// Gets the generation of the current lease.
    /// </summary>
    internal int Generation => Volatile.Read(ref _generation);

    /// <summary>
    /// Ends the lease <paramref name="generation"/> by advancing the generation.
    /// </summary>
    /// <param name="generation">The generation of the lease to end.</param>
    /// <returns>
    /// <see langword="true"/> for exactly one caller per lease; <see langword="false"/> when the lease has already ended
    /// (a second free of a copy, or a free after the manager released the buffer).
    /// </returns>
    internal bool TryRetire(int generation) =>
        Interlocked.CompareExchange(ref _generation, unchecked(generation + 1), generation) == generation;
}
