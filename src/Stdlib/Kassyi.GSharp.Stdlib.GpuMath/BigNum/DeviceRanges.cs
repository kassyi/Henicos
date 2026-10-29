namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Address-range checks between device buffers. Under CUDA unified addressing distinct allocations never share an
/// address, so two handles overlap exactly when their word ranges share a byte, whichever buffer objects wrap them.
/// </summary>
internal static class DeviceRanges
{
    /// <summary>
    /// Returns whether <c>[p0, p0 + 4 * words0)</c> and <c>[p1, p1 + 4 * words1)</c> share at least one byte; an empty
    /// range overlaps nothing.
    /// </summary>
    public static bool Overlaps(IntPtr p0, int words0, IntPtr p1, int words1) =>
        words0 > 0 && words1 > 0 &&
        (ulong)p0 < (ulong)p1 + (ulong)words1 * 4 &&
        (ulong)p1 < (ulong)p0 + (ulong)words0 * 4;

    /// <summary>Returns whether the words of two handles overlap; a default handle overlaps nothing.</summary>
    public static bool Overlaps(GpuBigIntMemoryHandle a, GpuBigIntMemoryHandle b) =>
        !a.IsDefault && !b.IsDefault && Overlaps(a.DevicePointer, a.NumWords, b.DevicePointer, b.NumWords);

    /// <summary>Returns whether the words of two views overlap.</summary>
    public static bool Overlaps(scoped GpuBigUIntView a, scoped GpuBigUIntView b) => Overlaps(a.Handle, b.Handle);
}
