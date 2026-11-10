namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Receives the leases and releases of <see cref="DefaultGpuMemoryManager"/>, in the order they happen.
/// </summary>
internal interface IGpuMemoryObserver
{
    /// <summary>A buffer of <paramref name="numWords"/> words at <paramref name="pointer"/> was leased.</summary>
    void Leased(IntPtr pointer, int numWords);

    /// <summary>The lease of the buffer at <paramref name="pointer"/> ended.</summary>
    void Released(IntPtr pointer, int numWords);
}
