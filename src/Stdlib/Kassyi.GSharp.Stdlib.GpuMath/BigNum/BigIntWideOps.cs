namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Big integer operations whose operands and result each have their own word count, on the GPU
/// (<see cref="GpuBigIntWideKernels"/>) or, in CPU fallback mode, with <see cref="BigInteger"/>.
/// </summary>
/// <remarks>
/// A missing word of an operand reads as zero. A result that does not fit in the result buffer, or an unsigned
/// difference below zero, is a domain error: on the GPU it is reported as <see cref="OverflowException"/> at the next
/// read of the workspace stream (<see cref="GpuWorkspace.ThrowIfDeviceErrors"/>), in CPU fallback mode it is thrown
/// at the call. The contents of the result buffer after a domain error are unspecified.
/// </remarks>
internal static class BigIntWideOps
{
    /// <summary><c>result = a + b</c>, or <c>a - b</c> when <paramref name="subtract"/> is set.</summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <param name="result">The result buffer; it may be an operand, but must not only partly overlap one.</param>
    /// <param name="subtract"><see langword="true"/> to subtract.</param>
    /// <exception cref="OverflowException">
    /// CPU fallback mode only: the result is below zero or does not fit.
    /// </exception>
    public static void AddSubtract(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result, bool subtract)
    {
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            var value = subtract ? a.ToBigInteger() - b.ToBigInteger() : a.ToBigInteger() + b.ToBigInteger();
            WriteModulo(value, result);
            if (value.Sign < 0)
            {
                throw new OverflowException("An unsigned difference is below zero.");
            }

            ThrowIfWiderThan(value, result.NumWords, subtract ? "A difference" : "A sum");
            return;
        }

        var workspace = a.Workspace;
        var words = Math.Max(Math.Max(a.NumWords, b.NumWords), result.NumWords);
        GpuBigIntWideKernels.LaunchAddSubWideKernelPtr(
            a.DevicePointer, a.NumWords, b.DevicePointer, b.NumWords, result.DevicePointer, result.NumWords,
            subtract ? 1 : 0, workspace.CorrectionStatusPointer,
            gridDimX: 1, blockDimX: SingleBlockSize(words), hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
    }

    /// <summary>
    /// <c>result = a op b</c> for <see cref="GpuBigIntWideKernels.BitwiseAnd"/>,
    /// <see cref="GpuBigIntWideKernels.BitwiseOr"/> and <see cref="GpuBigIntWideKernels.BitwiseXor"/>, or <c>~a</c>
    /// over the words of <paramref name="a"/> for <see cref="GpuBigIntWideKernels.BitwiseNot"/> (<paramref name="b"/>
    /// is then ignored). The result keeps its low words; it is not checked.
    /// </summary>
    public static void Bitwise(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result, int operation)
    {
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            var left = a.ToBigInteger();
            var right = operation == GpuBigIntWideKernels.BitwiseNot ? BigInteger.Zero : b.ToBigInteger();
            var value = operation switch
            {
                GpuBigIntWideKernels.BitwiseAnd => left & right,
                GpuBigIntWideKernels.BitwiseOr => left | right,
                GpuBigIntWideKernels.BitwiseXor => left ^ right,
                _ => ((BigInteger.One << (BigIntWidths.BitsPerWord * a.NumWords)) - 1) ^ left
            };
            WriteModulo(value, result);
            return;
        }

        var workspace = a.Workspace;
        var (gridSize, blockSize) = KernelGraphHandler.CalculateGridDim(result.NumWords);
        var bWords = operation == GpuBigIntWideKernels.BitwiseNot ? 0 : b.NumWords;
        GpuBigIntWideKernels.LaunchBitwiseWideKernelPtr(
            a.DevicePointer, a.NumWords, b.DevicePointer, bWords, result.DevicePointer, result.NumWords, operation,
            gridDimX: gridSize, blockDimX: blockSize, hStream: workspace.Stream);
    }

    /// <summary>
    /// Checks that <paramref name="value"/> fits in <paramref name="words"/> words: a non-zero word at or above them is
    /// a domain error.
    /// </summary>
    /// <exception cref="OverflowException">CPU fallback mode only: the value does not fit.</exception>
    public static void CheckFits(GpuBigUInt value, int words)
    {
        if (words >= value.NumWords)
        {
            return;
        }

        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            ThrowIfWiderThan(value.ToBigInteger(), words, "A value");
            return;
        }

        var workspace = value.Workspace;
        GpuBigIntWideKernels.LaunchFitCheckKernelPtr(value.DevicePointer, value.NumWords, words,
            workspace.CorrectionStatusPointer, gridDimX: 1, blockDimX: SingleBlockSize(value.NumWords - words),
            hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
    }

    /// <summary>
    /// One block, a multiple of 32 threads, for a kernel that loops over <paramref name="words"/> words.
    /// </summary>
    public static uint SingleBlockSize(int words) => (uint)((Math.Min(1024, Math.Max(words, 1)) + 31) / 32 * 32);

    private static void ThrowIfWiderThan(BigInteger value, int words, string what)
    {
        if (value.GetBitLength() > (long)BigIntWidths.BitsPerWord * words)
        {
            throw new OverflowException($"{what} does not fit in {words} words.");
        }
    }

    // Writes value modulo 2^(32 n) into the n words of result (CPU fallback: the buffers are host memory)
    private static void WriteModulo(BigInteger value, GpuBigUInt result)
    {
        var words = new uint[result.NumWords];
        (value & ((BigInteger.One << (BigIntWidths.BitsPerWord * result.NumWords)) - 1)).WriteLimbs(words);
        result.Workspace.Provider.CopyHostToDevice(result.DevicePointer, words, words.Length, result.Workspace.Stream);
    }
}
