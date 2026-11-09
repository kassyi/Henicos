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
            a.ToProxy(), a.NumWords, b.ToProxy(), b.NumWords, result.ToProxy(), result.NumWords,
            subtract ? 1 : 0, workspace.CorrectionStatusProxy,
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
            a.ToProxy(), a.NumWords, b.ToProxy(), bWords, result.ToProxy(), result.NumWords, operation,
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
        GpuBigIntWideKernels.LaunchFitCheckKernelPtr(value.ToProxy(), value.NumWords, words,
            workspace.CorrectionStatusProxy, gridDimX: 1, blockDimX: SingleBlockSize(value.NumWords - words),
            hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
    }

    /// <summary>
    /// The sum of two signed values given as sign and magnitude: <c>|result| = |(±a) + (±b)|</c>. Magnitudes of the
    /// same sign are added (<see cref="AddSubtract"/>, no read); different signs are subtracted in one kernel, which
    /// reads the borrow and zero flags once (<see cref="SubtractMagnitudes"/>).
    /// </summary>
    /// <param name="a">The magnitude of the first value.</param>
    /// <param name="aNegative">Whether the first value is negative.</param>
    /// <param name="b">The magnitude of the second value.</param>
    /// <param name="bNegative">Whether the second value is negative.</param>
    /// <param name="result">
    /// The result buffer. One word wider than the wider operand always holds the sum; it must not overlap the operands.
    /// </param>
    /// <returns>Whether the sum is negative. A sum of different signs that is zero is not negative.</returns>
    /// <exception cref="NotSupportedException">
    /// The signs differ and the workspace stream is capturing a CUDA graph.
    /// </exception>
    public static bool AddSigned(GpuBigUInt a, bool aNegative, GpuBigUInt b, bool bNegative, GpuBigUInt result)
    {
        if (aNegative == bNegative)
        {
            AddSubtract(a, b, result, subtract: false);
            return aNegative;
        }

        var (negated, isZero) = SubtractMagnitudes(a, b, result);
        return !isZero && (negated ? bNegative : aNegative);
    }

    /// <summary>
    /// <c>result = |a - b|</c>, and whether <c>b &gt; a</c> and whether the result is zero: the magnitude and the sign
    /// information of a sum of two values with different signs. Reads two words from the device, once.
    /// </summary>
    /// <param name="a">The first magnitude.</param>
    /// <param name="b">The second magnitude.</param>
    /// <param name="result">The result buffer, at least as wide as each operand; it must not overlap them.</param>
    /// <returns><c>Negated</c>: <c>b &gt; a</c>; <c>IsZero</c>: <c>a == b</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="result"/> is narrower than an operand.</exception>
    /// <exception cref="NotSupportedException">The workspace stream is capturing a CUDA graph.</exception>
    public static (bool Negated, bool IsZero) SubtractMagnitudes(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result)
    {
        if (result.NumWords < Math.Max(a.NumWords, b.NumWords))
        {
            throw new ArgumentException(
                $"The result buffer must be at least as wide as each operand. a: {a.NumWords}, b: {b.NumWords}, " +
                $"result: {result.NumWords} words.", nameof(result));
        }

        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            var value = a.ToBigInteger() - b.ToBigInteger();
            WriteModulo(BigInteger.Abs(value), result);
            return (value.Sign < 0, value.IsZero);
        }

        var workspace = a.Workspace;
        CaptureChecks.ThrowIfCapturing(workspace, "The sign of a sum of two values with different signs",
            "Evaluate the expression without a graph, or give the operands the same sign.");

        using var flags = workspace.RentTemp(2);
        GpuBigIntWideKernels.LaunchMagnitudeDifferenceKernelPtr(
            a.ToProxy(), a.NumWords, b.ToProxy(), b.NumWords, result.ToProxy(), result.NumWords,
            flags.ToProxy(),
            gridDimX: 1, blockDimX: SingleBlockSize(result.NumWords), hStream: workspace.Stream);

        var host = ArrayPool<uint>.Shared.Rent(2);
        try
        {
            workspace.Provider.CopyDeviceToHost(host, flags.DevicePointer, 2, workspace.Stream);
            return (host[0] != 0u, host[1] == 0u);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(host);
        }
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
