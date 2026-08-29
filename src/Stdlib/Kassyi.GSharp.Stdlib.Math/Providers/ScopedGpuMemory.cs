namespace Kassyi.GSharp.Stdlib.Math.Providers;

/// <summary>
/// A disposable stack-only ref struct representing a scoped allocation of GPU global device memory.
/// </summary>
/// <remarks>
/// Ensures deterministic deallocation of temporary GPU memory buffers upon exiting the using scope.
/// </remarks>
/// <remarks><para>Implements: <b>GEN-47</b></para></remarks>
public ref struct ScopedGpuMemory
{
    private readonly IGpuExecutionProvider _provider;

    /// <summary>Gets the raw native GPU device pointer (<c>CUdeviceptr</c>).</summary>
    public IntPtr Pointer
    {
        get; private set;
    }

    /// <summary>Allocates GPU memory for the specified number of limbs.</summary>
    public ScopedGpuMemory(IGpuExecutionProvider provider, int numWords)
    {
        _provider = provider;
        Pointer = provider.AllocateMemory(numWords);
    }

    /// <summary>Frees the allocated GPU memory buffer.</summary>
    public void Dispose()
    {
        if (Pointer != IntPtr.Zero)
        {
            _provider.FreeMemory(Pointer);
            Pointer = IntPtr.Zero;
        }
    }
}
