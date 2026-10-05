namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// A disposable stack-only ref struct representing a scoped allocation of GPU global device memory.
/// </summary>
/// <param name="provider">The GPU execution provider managing allocations.</param>
/// <param name="numWords">The number of 32-bit words to allocate.</param>
/// <remarks>
/// <para>
/// <b>Zero-Allocation:</b> Ensures deterministic deallocation of temporary GPU memory buffers upon exiting the scope without managed heap overhead.
/// </para>
/// </remarks>
public ref struct ScopedGpuMemory(IGpuExecutionProvider provider, int numWords)
{
    /// <summary>
    /// Gets the raw native GPU device pointer (<c>CUdeviceptr</c>).
    /// </summary>
    public IntPtr Pointer { get; private set; } = provider.AllocateMemory(numWords);

    /// <summary>
    /// Releases the allocated GPU memory buffer back to the provider.
    /// </summary>
    public void Dispose()
    {
        if (Pointer != IntPtr.Zero)
        {
            provider.FreeMemory(Pointer);
            Pointer = IntPtr.Zero;
        }
    }
}


