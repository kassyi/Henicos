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

    /// <summary>Returns whether the words of two array views overlap; a default view overlaps nothing.</summary>
    public static bool Overlaps(scoped GpuBigUIntArrayView a, scoped GpuBigUIntArrayView b) => Overlaps(a.Handle, b.Handle);

    /// <summary>Returns whether the words of <paramref name="a"/> overlap the values or the signs of <paramref name="b"/>.</summary>
    public static bool Overlaps(scoped GpuBigUIntArrayView a, scoped GpuBigIntArrayView b) =>
        Overlaps(a, b.Values) || Overlaps(a, b.Signs);

    /// <summary>
    /// Returns whether the significands, the exponents or the signs of <paramref name="a"/> overlap <paramref name="b"/>.
    /// </summary>
    public static bool Overlaps(scoped GpuBigFloatArrayView a, GpuBigIntMemoryHandle b) =>
        Overlaps(a.Significands.Handle, b) || Overlaps(a.Exponents.Handle, b) || Overlaps(a.Signs.Handle, b);

    /// <summary>Returns whether any component of <paramref name="a"/> overlaps any component of <paramref name="b"/>.</summary>
    /// <remarks>
    /// The pairs of different components (significands over exponents) have no test: only the internal constructors of
    /// a float view can build them.
    /// </remarks>
    public static bool Overlaps(scoped GpuBigFloatArrayView a, scoped GpuBigFloatArrayView b) =>
        Overlaps(a, b.Significands.Handle) || Overlaps(a, b.Exponents.Handle) || Overlaps(a, b.Signs.Handle);
}
