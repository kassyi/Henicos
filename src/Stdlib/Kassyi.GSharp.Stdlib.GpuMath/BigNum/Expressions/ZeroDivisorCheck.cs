namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The check of an element-wise division for a zero divisor element: the kernel records the lowest index of an element
/// whose divisor is zero, and the host throws <see cref="DivideByZeroException"/> when that element is one of the
/// evaluated elements, as C# integer division does.
/// </summary>
/// <remarks>
/// The kernel writes <c>~index</c> with an atomic max into one word cleared to 0, so 0 means no zero divisor and the
/// largest complement is the lowest index. An element past a device count (after a <c>Where</c>) is not an element of
/// the vector, and its divisor is ignored.
/// </remarks>
internal static class ZeroDivisorCheck
{
    /// <summary>Lends the word the kernel writes, cleared on the workspace stream.</summary>
    /// <exception cref="NotSupportedException">The workspace stream is capturing a CUDA graph.</exception>
    public static GpuBigUIntArrayView Open(GpuWorkspace workspace, VectorResultCache cache)
    {
        if (DeviceCounted.IsCapturing(workspace))
        {
            throw new NotSupportedException(
                "An element-wise division cannot be part of a CUDA graph: a zero divisor is read back to the host. " +
                "Evaluate it directly.");
        }

        var word = cache.Scan(workspace, 1);
        workspace.Provider.ClearMemoryAsync(word.DevicePointer, 1, workspace.Stream);
        return word;
    }

    /// <summary>Reads the word after the launch: one synchronization of the workspace stream.</summary>
    /// <exception cref="DivideByZeroException">The divisor of one of the first <paramref name="count"/> elements is zero.</exception>
    public static void ThrowIfFound(GpuWorkspace workspace, GpuBigUIntArrayView word, DeviceElementCount count)
    {
        var mark = workspace.Provider.ReadWord(word.DevicePointer, 0, workspace.Stream);
        if (mark != 0u && ~mark < (uint)count.Read(workspace))
        {
            throw new DivideByZeroException();
        }
    }
}
