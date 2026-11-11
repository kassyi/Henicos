namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The check of a division for a zero divisor. C# divides only the elements of a sequence, so a zero divisor throws
/// <see cref="DivideByZeroException"/> only when it divides one of them. For an element-wise division the kernel
/// records the lowest index of an element whose divisor is zero, and the host throws when that element is one of the
/// evaluated elements; for a scalar divisor the host throws when the vector has at least one element.
/// </summary>
/// <remarks>
/// The kernel writes <c>~index</c> with an atomic max into one word cleared to 0, so 0 means no zero divisor and the
/// largest complement is the lowest index. An element past a device count (after a <c>Where</c>) is not an element of
/// the vector, and its divisor is ignored.
/// </remarks>
internal static class ZeroDivisorCheck
{
    /// <summary>Refuses a CUDA graph capture before the zero test of a scalar divisor reads the scalar.</summary>
    /// <exception cref="NotSupportedException">The workspace stream is capturing a CUDA graph.</exception>
    public static void ThrowIfCapturingScalarTest(GpuWorkspace workspace) =>
        CaptureChecks.ThrowIfCapturing(workspace, "The zero test of a scalar divisor");

    /// <summary>
    /// Throws for a zero scalar divisor when the vector has at least one element: reads the count, one synchronization
    /// of the workspace stream when it is on the device. A vector with no element divides nothing.
    /// </summary>
    /// <exception cref="DivideByZeroException">The count is positive.</exception>
    public static void ThrowIfAnyElement(GpuWorkspace workspace, DeviceElementCount count)
    {
        if (count.Read(workspace) > 0)
        {
            throw new DivideByZeroException();
        }
    }

    /// <summary>Lends the word the kernel writes, cleared on the workspace stream.</summary>
    /// <exception cref="NotSupportedException">The workspace stream is capturing a CUDA graph.</exception>
    public static GpuBigUIntArrayView Open(GpuWorkspace workspace, VectorResultCache cache)
    {
        CaptureChecks.ThrowIfCapturing(workspace, "A zero divisor of an element-wise division",
            "Evaluate it directly.");

        var word = cache.Scan(workspace, 1);
        workspace.Provider.ClearMemoryAsync(word.DevicePointer, 1, workspace.Stream);
        return word;
    }

    /// <summary>Reads the word after the launch: one synchronization of the workspace stream.</summary>
    /// <exception cref="DivideByZeroException">
    /// The divisor of one of the first <paramref name="count"/> elements is zero.
    /// </exception>
    public static void ThrowIfFound(GpuWorkspace workspace, GpuBigUIntArrayView word, DeviceElementCount count)
    {
        var mark = workspace.Provider.ReadWord(word.DevicePointer, 0, workspace.Stream);
        if (mark != 0u && ~mark < (uint)count.Read(workspace))
        {
            throw new DivideByZeroException();
        }
    }
}
