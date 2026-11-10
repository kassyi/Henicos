namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// The rules every big-integer operation applies to the result buffer a caller passes: a buffer narrower than the
/// operation's minimum is rejected before anything is launched; a wider one receives the result in its first words
/// and zeros above them.
/// </summary>
internal static class ResultBuffers
{
    /// <summary>Throws when <paramref name="result"/> has fewer than <paramref name="minimumWords"/> words.</summary>
    /// <param name="result">The result buffer.</param>
    /// <param name="minimumWords">The minimum of the operation.</param>
    /// <param name="operation">The name of the operation for the message.</param>
    /// <exception cref="ArgumentException">The result buffer is narrower than the minimum.</exception>
    public static void ThrowIfNarrower(GpuBigUInt result, int minimumWords, string operation)
    {
        if (result.NumWords < minimumWords)
        {
            throw new ArgumentException(
                $"{operation}: the result buffer must have at least {minimumWords} words; it has {result.NumWords}.",
                "resultBuffer");
        }
    }

    /// <summary>The first <paramref name="words"/> words of <paramref name="result"/>, borrowed.</summary>
    public static GpuBigUInt Head(GpuBigUInt result, int words) =>
        words == result.NumWords ? result : new GpuBigUInt(result.Workspace, result.Handle.Slice(0, words));

    /// <summary>
    /// Clears the words of <paramref name="result"/> above its first <paramref name="words"/> (a memset, which a CUDA
    /// graph capture records).
    /// </summary>
    public static void ClearAbove(GpuBigUInt result, int words)
    {
        if (result.NumWords > words)
        {
            result.Workspace.Provider.ClearMemoryAsync(IntPtr.Add(result.DevicePointer, words * sizeof(uint)),
                result.NumWords - words, result.Workspace.Stream);
        }
    }
}
