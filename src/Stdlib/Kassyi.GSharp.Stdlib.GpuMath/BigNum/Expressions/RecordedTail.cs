namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Sets to zero the elements of an output from a recorded element on (<see cref="PendingThrows.RecordOrThrow"/>), which
/// the node does not compute.
/// </summary>
/// <remarks>
/// Left alone, those elements keep what the buffer held from an earlier evaluation. That cannot change the answer of
/// <c>Any</c> / <c>All</c>, but a later operation that runs on the host (a float <c>Exp</c> or <c>Pow</c>, a division
/// of big integers) would then run on values the inputs do not bound, an exponent most of all, and could take a time
/// and allocate memory that the inputs do not bound either. Zero is a value every later operation handles at a fixed
/// cost.
/// </remarks>
internal static class RecordedTail
{
    /// <summary>Sets the values and signs of the elements from <paramref name="start"/> to zero.</summary>
    /// <param name="workspace">The workspace whose stream clears the memory.</param>
    /// <param name="destination">The output of the node.</param>
    /// <param name="start">The recorded element.</param>
    /// <param name="count">The number of elements the node evaluates.</param>
    public static void Clear(GpuWorkspace workspace, GpuBigIntArrayView destination, int start, int count)
    {
        if (start >= count)
        {
            return;
        }

        var tail = destination.Slice(start, count - start);
        ClearWords(workspace, tail.Values);
        ClearWords(workspace, tail.Signs);
    }

    /// <summary>
    /// Sets the elements from <paramref name="start"/> to the float zero (zero significand, exponent and sign).
    /// </summary>
    /// <param name="workspace">The workspace whose stream clears the memory.</param>
    /// <param name="destination">The output of the node.</param>
    /// <param name="start">The recorded element.</param>
    /// <param name="count">The number of elements the node evaluates.</param>
    public static void Clear(GpuWorkspace workspace, GpuBigFloatArrayView destination, int start, int count)
    {
        if (start >= count)
        {
            return;
        }

        var tail = destination.Slice(start, count - start);
        ClearWords(workspace, tail.Significands);
        ClearWords(workspace, tail.Exponents);
        ClearWords(workspace, tail.Signs);
    }

    private static void ClearWords(GpuWorkspace workspace, GpuBigUIntArrayView words) =>
        workspace.Provider.ClearMemoryAsync(words.DevicePointer, words.TotalWords, workspace.Stream);
}
