namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Receives the leases and releases of <see cref="DefaultGpuMemoryManager"/>, in the order they happen.
/// </summary>
internal interface IGpuMemoryObserver
{
    /// <summary>A buffer was leased.</summary>
    /// <param name="pointer">The device pointer of the buffer.</param>
    /// <param name="numWords">The words of the buffer.</param>
    void Leased(IntPtr pointer, int numWords);

    /// <summary>The lease of a buffer ended.</summary>
    /// <param name="pointer">The device pointer of the buffer.</param>
    /// <param name="numWords">The words of the buffer.</param>
    void Released(IntPtr pointer, int numWords);
}
