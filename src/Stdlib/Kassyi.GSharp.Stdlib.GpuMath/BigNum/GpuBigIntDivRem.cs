
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides static arbitrary-precision division, remainder, modulo, and root extraction algorithms on the GPU using Newton-Raphson approximation and reciprocal multiplication.
/// </summary>
/// <remarks>
/// <para><b>Algorithms:</b> Division is evaluated as $A / B \approx A \times \text{Reciprocal}(B)$ using parallel Newton-Raphson reciprocals on the GPU, followed by fine-grained underflow/remainder correction loops.</para>
/// <para><b>Thread Safety:</b> Static methods are thread-safe given distinct GPU memory buffers.</para>
/// </remarks>
public static partial class GpuBigIntDivRem
{
    private readonly record struct BarrettCorrectionContext(
        GpuWorkspace Workspace,
        int EvalWords,
        GpuBigUInt QuotientBuffer,
        GpuBigUInt RemainderExtended,
        GpuBigUInt BPaddedExtended);

    // [WHY] Validates buffer lengths at the host boundary before GPU kernel dispatch to avoid silent CUDA out-of-bounds writes.
    [ExcludeFromCodeCoverage]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowIfSizeMismatch(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        if (leftOperand.NumWords != rightOperand.NumWords || leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    [ExcludeFromCodeCoverage]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowIfSizeMismatch(GpuBigUInt leftOperand, GpuBigUInt resultBuffer)
    {
        if (leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must have the same size as input buffer. a: {leftOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    // [WHY] DivRem writes the whole quotient and remainder; a shorter buffer would silently drop their high words.
    // The quotient of an aBits-bit dividend by a bBits-bit divisor (bBits >= 1) has at most aBits - bBits + 1 bits.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowIfDivRemBufferTooSmall(GpuBigUInt rightOperand, int aActualBits, int bActualBits,
        GpuBigUInt quotientBuffer, GpuBigUInt remainderBuffer)
    {
        var divisorWords = rightOperand.NumWords;
        var requiredQuotientWords = Math.Max((aActualBits - bActualBits + 1 + 31) / 32, 1);
        if (quotientBuffer.NumWords < requiredQuotientWords)
        {
            throw new ArgumentException(
                $"The quotient buffer must hold at least {requiredQuotientWords} words. a: {aActualBits} bits, b: {bActualBits} bits, quotient: {quotientBuffer.NumWords} words.");
        }

        if (remainderBuffer.NumWords < divisorWords)
        {
            throw new ArgumentException(
                $"The remainder buffer must hold at least {divisorWords} words. b: {divisorWords}, remainder: {remainderBuffer.NumWords} words.");
        }
    }

    /// <summary>
    /// Computes the reciprocal <c>floor(2^targetBits / b)</c> of the operand <c>b</c> using Newton-Raphson iterations on
    /// the GPU, followed by an exact correction on the device.
    /// </summary>
    /// <param name="targetOperand">The divisor <c>b</c>. It need not be normalized: the initial approximation is built
    /// from its bit length, and the big-float <c>Reciprocal</c> passes significands with leading zero bits or words.</param>
    /// <param name="resultBuffer">Receives exactly <c>floor(2^targetBits / b)</c>; it must hold
    /// at least <c>(targetBits + 31) / 32</c> words.</param>
    /// <param name="targetBits">The exponent <c>T</c> of the numerator <c>2^T</c>; <c>2^T &gt;= 4b</c>. The result
    /// must fit in <c>(T + 31) / 32</c> words: <c>floor(2^T / b) &lt;= 2^(T - 1)</c> does for every <c>b &gt;= 2</c>, but
    /// <c>2^T / 1</c> needs <c>T + 1</c> bits, so a caller that may pass <c>b = 1</c> uses a <c>T</c> that is not a
    /// multiple of 32 (the big-float <c>Reciprocal</c> uses <c>32·w - 1</c>).</param>
    /// <param name="marginWords">Passed on to the provider's context; the reciprocal kernel does not read it, since
    /// <paramref name="targetBits"/> sets the precision.</param>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="targetOperand"/> is zero.</exception>
    /// <remarks>
    /// The Newton-Raphson result is <c>floor(2^T / b)</c> or one less; the kernel then computes <c>r = 2^T - b * x</c>
    /// and moves <c>x</c> by the steps that bring <c>r</c> into <c>[0, b)</c>, without a host synchronization. If 1000 steps
    /// do not suffice, the next read of the workspace stream throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    internal static void Reciprocal(
        scoped GpuBigUIntView targetOperand,
        scoped GpuBigUIntView resultBuffer,
        int targetBits,
        int marginWords = 0
    ) =>
        ReciprocalCore(targetOperand.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt(), targetBits, marginWords);

    // Reciprocal on the owned type; the internal method above takes views and borrows them
    private static void ReciprocalCore(GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int targetBits,
        int marginWords = 0)
    {
        var n = targetOperand.NumWords;
        var numBits = n * 32;
        var marginBits = marginWords * 32;
        ComputeInitialGuess(targetOperand, resultBuffer, targetBits, isSquareRoot: false);

        var currentBits = 63; // Initial guess has ~63 bits of precision
        var iterations = CalculateNewtonRaphsonIterationCount(currentBits, targetBits);

        if (targetOperand.Workspace.Provider is { } provider)
        {
            provider.ReciprocalMega(targetOperand.Workspace, targetOperand, resultBuffer, marginWords, numBits,
                marginBits, iterations, targetBits);
        }
        else
        {
            throw new NotSupportedException("The current provider does not support ReciprocalMega.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateNewtonRaphsonIterationCount(int currentBits, int targetBits)
    {
        var iterations = 0;
        while (currentBits < targetBits)
        {
            currentBits *= 2;
            iterations++;
        }

        return iterations + 1; // One extra iteration for safety to ensure full convergence
    }

    /// <summary>
    /// Computes both the quotient and the remainder of two arbitrary-precision unsigned integers on the GPU.
    /// </summary>
    /// <param name="leftOperand">The dividend operand ($A$).</param>
    /// <param name="rightOperand">The divisor operand ($B$).</param>
    /// <param name="quotientBuffer">The destination buffer receiving the quotient ($\lfloor A / B \rfloor$). It must hold
    /// at least as many words as the quotient of these values may need, <c>max(ceil((aBits - bBits + 1) / 32), 1)</c>
    /// for the bit lengths <c>aBits</c> and <c>bBits</c> of the values; a buffer as long as
    /// <paramref name="leftOperand"/> is always enough. Words above the quotient are set to zero.</param>
    /// <param name="remainderBuffer">The destination buffer receiving the remainder ($A \bmod B$). It must hold at least
    /// <c>n</c> words; words above the remainder are set to zero.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="quotientBuffer"/> or <paramref name="remainderBuffer"/> is shorter than stated above, or when the two overlap.</exception>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="rightOperand"/> is zero.</exception>
    /// <remarks>
    /// <para><b>Algorithm:</b> Evaluates division via normalized reciprocal multiplication on GPU followed by quotient-remainder correction.</para>
    /// <para>Either output may be the dividend or the divisor, or overlap them: as GMP's <c>mpz_tdiv_qr</c>, an
    /// operand that shares memory with an output is copied first, and the result is the one of separate buffers. The
    /// quotient and the remainder must not overlap each other.</para>
    /// </remarks>
    public static void DivRem(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView quotientBuffer,
        scoped GpuBigUIntView remainderBuffer
    )
    {
        if (DeviceRanges.Overlaps(quotientBuffer, remainderBuffer))
        {
            throw new ArgumentException("The quotient and remainder buffers must not overlap.", nameof(remainderBuffer));
        }

        // The quotient is written before the dividend and the divisor are read again (Reduce, the power-of-two paths),
        // so an operand that shares memory with an output is read from a copy
        using var leftCopy = CopyIfOverlapsOutput(leftOperand, quotientBuffer, remainderBuffer);
        using var rightCopy = CopyIfOverlapsOutput(rightOperand, quotientBuffer, remainderBuffer);
        DivRemCore(
            leftCopy.Handle.IsDefault ? leftOperand.AsBorrowedBigUInt() : leftCopy,
            rightCopy.Handle.IsDefault ? rightOperand.AsBorrowedBigUInt() : rightCopy,
            quotientBuffer.AsBorrowedBigUInt(),
            remainderBuffer.AsBorrowedBigUInt()
        );
    }

    // An owned copy of operand when it shares memory with an output; otherwise the default value, which owns nothing
    private static GpuBigUInt CopyIfOverlapsOutput(scoped GpuBigUIntView operand, scoped GpuBigUIntView output0,
        scoped GpuBigUIntView output1)
    {
        if (!DeviceRanges.Overlaps(operand, output0) && !DeviceRanges.Overlaps(operand, output1))
        {
            return default;
        }

        var copy = new GpuBigUInt(operand.Workspace, operand.NumWords);
        try
        {
            copy.CopyFrom(operand);
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    // Reduce and ReduceZeroSync do not copy their inputs: an output that overlaps an input, or the other output, would
    // be read after it was written
    private static void ThrowIfReduceOutputsOverlap(scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand, scoped GpuBigUIntView reciprocalOfB, scoped GpuBigUIntView quotientBuffer,
        scoped GpuBigUIntView remainderBuffer)
    {
        if (DeviceRanges.Overlaps(quotientBuffer, remainderBuffer) ||
            OverlapsInput(quotientBuffer, leftOperand, rightOperand, reciprocalOfB) ||
            OverlapsInput(remainderBuffer, leftOperand, rightOperand, reciprocalOfB))
        {
            throw new ArgumentException(
                "The quotient and remainder buffers must not overlap each other or an input.", nameof(quotientBuffer));
        }
    }

    private static bool OverlapsInput(scoped GpuBigUIntView output, scoped GpuBigUIntView input0,
        scoped GpuBigUIntView input1, scoped GpuBigUIntView input2) =>
        DeviceRanges.Overlaps(output, input0) || DeviceRanges.Overlaps(output, input1) ||
        DeviceRanges.Overlaps(output, input2);

    // DivRem on the owned type; the public method above takes views and borrows them
    private static void DivRemCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt quotientBuffer, GpuBigUInt remainderBuffer)
    {
        var nA = leftOperand.NumWords;
        var n = rightOperand.NumWords;
        var bActualBits = GetActualBits(rightOperand);
        if (bActualBits == 0)
        {
            throw new DivideByZeroException();
        }

        var aActualBits = GetActualBits(leftOperand);
        ThrowIfDivRemBufferTooSmall(rightOperand, aActualBits, bActualBits, quotientBuffer, remainderBuffer);

        if (aActualBits < bActualBits)
        {
            if (remainderBuffer.NumWords > nA)
            {
                remainderBuffer.Clear();
            }

            // The remainder (the dividend) is taken before the quotient is cleared, as GMP's tdiv_qr does
            remainderBuffer.CopyFrom(leftOperand, numWords: Math.Min(remainderBuffer.NumWords, nA));
            quotientBuffer.Clear();
            return;
        }

        if (TryDivRemSmallOrPowerOfTwo(leftOperand, rightOperand, bActualBits, quotientBuffer, remainderBuffer))
        {
            return;
        }

        var lz = n * 32 - bActualBits;

        using var bNorm = new GpuBigUInt(rightOperand.Workspace, n);
        GpuBigIntMath.ShiftLeft(rightOperand, lz, bNorm);

        var marginBits = 512;
        var marginWords = marginBits / 32;
        var aActualWords = (aActualBits + 31) / 32;
        var targetBits = n * 32 + aActualWords * 32 + marginBits;
        var targetWords = (targetBits + 31) / 32 + 1;

        using var reciprocalNorm = new GpuBigUInt(rightOperand.Workspace, targetWords);
        reciprocalNorm.Clear();
        Reciprocal(bNorm, reciprocalNorm, targetBits, marginWords);

        using var reciprocalOfB = new GpuBigUInt(rightOperand.Workspace, targetWords);
        GpuBigIntMath.ShiftLeft(reciprocalNorm, lz, reciprocalOfB);

        Reduce(leftOperand, rightOperand, reciprocalOfB, targetBits, aActualWords, quotientBuffer, remainderBuffer);
    }

    private static bool TryDivRemSmallOrPowerOfTwo(GpuBigUInt leftOperand, GpuBigUInt rightOperand, int bActualBits,
        GpuBigUInt quotientBuffer, GpuBigUInt remainderBuffer)
    {
        var workspace = leftOperand.Workspace;
        var nA = leftOperand.NumWords;
        var n = rightOperand.NumWords;

        if (bActualBits <= 32)
        {
            var divisor = workspace.Provider.ReadWord(rightOperand.DevicePointer, 0, workspace.Stream);
            if (divisor == 1u)
            {
                quotientBuffer.Clear();
                quotientBuffer.CopyFrom(leftOperand, numWords: Math.Min(quotientBuffer.NumWords, nA));
                remainderBuffer.Clear();
                return true;
            }

            if ((divisor & (divisor - 1u)) == 0u)
            {
                var shift = bActualBits - 1;
                if (quotientBuffer.NumWords == nA)
                {
                    GpuBigIntMath.ShiftRight(leftOperand, shift, quotientBuffer);
                }
                else
                {
                    using var tempQ = workspace.RentTemp(nA);
                    GpuBigIntMath.ShiftRight(leftOperand, shift, tempQ);
                    quotientBuffer.Clear();
                    quotientBuffer.CopyFrom(tempQ, numWords: Math.Min(quotientBuffer.NumWords, nA));
                }

                var remWord = workspace.Provider.ReadWord(leftOperand.DevicePointer, 0, workspace.Stream) & (divisor - 1u);
                remainderBuffer.Clear();
                if (remWord != 0u && remainderBuffer.NumWords > 0)
                {
                    workspace.Provider.WriteWord(remainderBuffer.DevicePointer, 0, remWord, workspace.Stream);
                }

                return true;
            }

            uint rem;
            if (quotientBuffer.NumWords == nA)
            {
                rem = GpuBigIntMath.DivideByUInt32(leftOperand, divisor, quotientBuffer);
            }
            else
            {
                using var tempQ = workspace.RentTemp(nA);
                rem = GpuBigIntMath.DivideByUInt32(leftOperand, divisor, tempQ);
                quotientBuffer.Clear();
                quotientBuffer.CopyFrom(tempQ, numWords: Math.Min(quotientBuffer.NumWords, nA));
            }

            remainderBuffer.Clear();
            if (rem != 0u && remainderBuffer.NumWords > 0)
            {
                workspace.Provider.WriteWord(remainderBuffer.DevicePointer, 0, rem, workspace.Stream);
            }

            return true;
        }

        // Fast path for multi-word power-of-2 divisors (b = 2^k, k >= 32): b & (b - 1) == 0.
        // Reading the top word first rejects almost every other divisor with one read, before the kernels below.
        var topBit = bActualBits - 1;
        var topWord = workspace.Provider.ReadWord(rightOperand.DevicePointer, topBit / 32, workspace.Stream);
        if (topWord != 1u << (topBit % 32))
        {
            return false;
        }

        using var mask = workspace.RentTemp(n);
        GpuBigIntMath.Sub(rightOperand, 1u, mask);
        using var bAndMask = workspace.RentTemp(n);
        GpuBigIntMath.BitwiseAnd(rightOperand, mask, bAndMask);
        if (!bAndMask.IsZero())
        {
            return false;
        }

        if (quotientBuffer.NumWords == nA)
        {
            GpuBigIntMath.ShiftRight(leftOperand, topBit, quotientBuffer);
        }
        else
        {
            using var tempQ = workspace.RentTemp(nA);
            GpuBigIntMath.ShiftRight(leftOperand, topBit, tempQ);
            quotientBuffer.Clear();
            quotientBuffer.CopyFrom(tempQ, numWords: Math.Min(quotientBuffer.NumWords, nA));
        }

        var fullWords = topBit / 32;
        var remBits = topBit % 32;
        remainderBuffer.Clear();
        var copyWords = Math.Min(remainderBuffer.NumWords, Math.Min(nA, fullWords));
        if (copyWords > 0)
        {
            remainderBuffer.CopyFrom(leftOperand, numWords: copyWords);
        }

        if (remBits > 0 && fullWords < nA && fullWords < remainderBuffer.NumWords)
        {
            var topRemWord = workspace.Provider.ReadWord(leftOperand.DevicePointer, fullWords, workspace.Stream) &
                             ((1u << remBits) - 1u);
            if (topRemWord != 0u)
            {
                workspace.Provider.WriteWord(remainderBuffer.DevicePointer, fullWords, topRemWord, workspace.Stream);
            }
        }

        return true;
    }

    /// <summary>
    /// Performs a Barrett reduction using a precomputed reciprocal.
    /// </summary>
    /// <param name="leftOperand">The dividend.</param>
    /// <param name="rightOperand">The divisor.</param>
    /// <param name="reciprocalOfB">The precomputed reciprocal of the divisor.</param>
    /// <param name="targetBits">The target precision in bits.</param>
    /// <param name="aActualWords">The actual number of words in the dividend.</param>
    /// <param name="quotientBuffer">The buffer to store the computed quotient.</param>
    /// <param name="remainderBuffer">The buffer to store the computed remainder.</param>
    /// <exception cref="ArgumentException">The quotient and remainder buffers overlap each other or an input; unlike
    /// <see cref="DivRem"/>, this building block does not copy its inputs.</exception>
    public static void Reduce(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView reciprocalOfB,
        int targetBits,
        int aActualWords,
        scoped GpuBigUIntView quotientBuffer,
        scoped GpuBigUIntView remainderBuffer
    )
    {
        ThrowIfReduceOutputsOverlap(leftOperand, rightOperand, reciprocalOfB, quotientBuffer, remainderBuffer);
        ReduceCore(
            leftOperand.AsBorrowedBigUInt(),
            rightOperand.AsBorrowedBigUInt(),
            reciprocalOfB.AsBorrowedBigUInt(),
            targetBits,
            aActualWords,
            quotientBuffer.AsBorrowedBigUInt(),
            remainderBuffer.AsBorrowedBigUInt()
        );
    }

    // Reduce on the owned type; the public method above takes views and borrows them
    private static void ReduceCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt reciprocalOfB,
        int targetBits, int aActualWords, GpuBigUInt quotientBuffer, GpuBigUInt remainderBuffer)
    {
        var nA = leftOperand.NumWords;
        var n = rightOperand.NumWords;
        var targetWords = reciprocalOfB.NumWords;

        using var productWithReciprocal = new GpuBigUInt(rightOperand.Workspace, nA + targetWords);
        GpuBigIntMath.Mul(leftOperand, reciprocalOfB, productWithReciprocal);

        // The estimate fills at most nA words; the words above it take part in q * b and must be zero.
        if (quotientBuffer.NumWords > nA)
        {
            quotientBuffer.Clear();
        }

        quotientBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: targetBits / 32,
            numWords: Math.Min(quotientBuffer.NumWords, nA));

        using var qb = new GpuBigUInt(rightOperand.Workspace, quotientBuffer.NumWords + n);
        GpuBigIntMath.Mul(quotientBuffer, rightOperand, qb);

        var qbActualWords = (GetActualBits(qb) + 31) / 32;
        var evalWords = Math.Max(aActualWords, qbActualWords) + 1; // +1 to allow borrow logic

        using var qbTrunc = new GpuBigUInt(rightOperand.Workspace, evalWords);
        qbTrunc.CopyFrom(qb, numWords: Math.Min(quotientBuffer.NumWords + n, evalWords));

        using var leftOperandExtended = new GpuBigUInt(rightOperand.Workspace, evalWords);
        leftOperandExtended.Clear();
        leftOperandExtended.CopyFrom(leftOperand, numWords: Math.Min(nA, evalWords));

        using var remainderExtended = new GpuBigUInt(rightOperand.Workspace, evalWords);
        GpuBigIntMath.Sub(leftOperandExtended, qbTrunc, remainderExtended);

        var topWord = remainderExtended.GetTopWords(1)[0];
        var underflow = (topWord & 0x80000000u) != 0;

        if (underflow)
        {
            using var bPaddedExtended = new GpuBigUInt(rightOperand.Workspace, evalWords);
            bPaddedExtended.Clear();
            bPaddedExtended.CopyFrom(rightOperand, numWords: Math.Min(n, evalWords));

            var correctionCtx = new BarrettCorrectionContext(
                rightOperand.Workspace, evalWords, quotientBuffer, remainderExtended, bPaddedExtended);
            CorrectUnderflow(in correctionCtx);
        }
        else
        {
            using var bPaddedExtended = new GpuBigUInt(rightOperand.Workspace, evalWords);
            bPaddedExtended.Clear();
            bPaddedExtended.CopyFrom(rightOperand, numWords: Math.Min(rightOperand.NumWords, evalWords));

            var correctionCtx = new BarrettCorrectionContext(
                rightOperand.Workspace, evalWords, quotientBuffer, remainderExtended, bPaddedExtended);
            CorrectRemainder(in correctionCtx);
        }

        remainderBuffer.Clear();
        remainderBuffer.CopyFrom(remainderExtended, numWords: Math.Min(remainderBuffer.NumWords, evalWords));
    }

    /// <summary>
    /// Performs a zero-sync Barrett reduction using a precomputed reciprocal.
    /// This method does not synchronize with the CPU for remainder correction, enabling CUDA Graph capture.
    /// </summary>
    /// <remarks>
    /// The quotient estimate <c>floor(a * reciprocalOfB / 2^targetBits)</c> is corrected on the device, and the
    /// quotient and the remainder are exact (<c>q * b + r == a</c>, <c>0 &lt;= r &lt; b</c>) when the estimate is within
    /// 1000 steps of the quotient. Otherwise the kernel records the failure in the correction status of the workspace,
    /// and the next read of the workspace stream throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    /// <param name="leftOperand">The left operand.</param>
    /// <param name="rightOperand">The right operand.</param>
    /// <param name="reciprocalOfB">The precomputed reciprocal of the right operand.</param>
    /// <param name="targetBits">The target precision in bits.</param>
    /// <param name="aActualWords">The actual word count of the left operand.</param>
    /// <param name="quotientBuffer">The buffer to store the quotient result.</param>
    /// <param name="remainderBuffer">The buffer to store the remainder result.</param>
    /// <exception cref="ArgumentException">The quotient and remainder buffers overlap each other or an input; unlike
    /// <see cref="DivRem"/>, this building block does not copy its inputs.</exception>
    public static void ReduceZeroSync(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView reciprocalOfB,
        int targetBits,
        int aActualWords,
        scoped GpuBigUIntView quotientBuffer,
        scoped GpuBigUIntView remainderBuffer
    )
    {
        ThrowIfReduceOutputsOverlap(leftOperand, rightOperand, reciprocalOfB, quotientBuffer, remainderBuffer);
        ReduceZeroSyncCore(
            leftOperand.AsBorrowedBigUInt(),
            rightOperand.AsBorrowedBigUInt(),
            reciprocalOfB.AsBorrowedBigUInt(),
            targetBits,
            aActualWords,
            quotientBuffer.AsBorrowedBigUInt(),
            remainderBuffer.AsBorrowedBigUInt()
        );
    }

    // ReduceZeroSync on the owned type; the public method above takes views and borrows them
    private static void ReduceZeroSyncCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt reciprocalOfB,
        int targetBits, int aActualWords, GpuBigUInt quotientBuffer, GpuBigUInt remainderBuffer)
    {
        var nA = leftOperand.NumWords;
        var n = rightOperand.NumWords;
        var targetWords = reciprocalOfB.NumWords;

        using var productWithReciprocal = new GpuBigUInt(rightOperand.Workspace, nA + targetWords);
        GpuBigIntMath.Mul(leftOperand, reciprocalOfB, productWithReciprocal);

        // As in Reduce: the words above the estimate take part in q * b. The clear is a stream-ordered upload, like the
        // other clears in this method, and needs no host synchronization.
        if (quotientBuffer.NumWords > nA)
        {
            quotientBuffer.Clear();
        }

        quotientBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: targetBits / 32,
            numWords: Math.Min(quotientBuffer.NumWords, nA));

        using var qb = new GpuBigUInt(rightOperand.Workspace, quotientBuffer.NumWords + n);
        GpuBigIntMath.Mul(quotientBuffer, rightOperand, qb);

        // Zero-sync safe bound: use the full allocated size of qb instead of dynamically reading actual bits
        var evalWords = quotientBuffer.NumWords + n + 1;

        using var qbTrunc = new GpuBigUInt(rightOperand.Workspace, evalWords);
        qbTrunc.Clear();
        qbTrunc.CopyFrom(qb, numWords: Math.Min(quotientBuffer.NumWords + n, evalWords));

        using var leftOperandExtended = new GpuBigUInt(rightOperand.Workspace, evalWords);
        leftOperandExtended.Clear();
        leftOperandExtended.CopyFrom(leftOperand, numWords: Math.Min(nA, evalWords));

        using var remainderExtended = new GpuBigUInt(rightOperand.Workspace, evalWords);
        GpuBigIntMath.Sub(leftOperandExtended, qbTrunc, remainderExtended);

        using var bPaddedExtended = new GpuBigUInt(rightOperand.Workspace, evalWords);
        bPaddedExtended.Clear();
        bPaddedExtended.CopyFrom(rightOperand, numWords: Math.Min(n, evalWords));

        // Zero-sync correction using single block kernel: the remainder and the quotient move by the same steps.
        GpuBigIntDivRemKernels.LaunchBarrettCorrectionKernelPtr(
            remainderExtended.DevicePointer, bPaddedExtended.DevicePointer, quotientBuffer.DevicePointer, evalWords,
            quotientBuffer.NumWords, rightOperand.Workspace.CorrectionStatusPointer, gridDimX: 1, blockDimX: 1024,
            hStream: rightOperand.Workspace.Stream);
        rightOperand.Workspace.CorrectionStatus?.MarkPending();

        remainderBuffer.Clear();
        remainderBuffer.CopyFrom(remainderExtended, numWords: Math.Min(remainderBuffer.NumWords, evalWords));
    }

    [ExcludeFromCodeCoverage]
    private static void CorrectUnderflow(in BarrettCorrectionContext ctx)
    {
        var loopCount = 0;
        using var one = GpuBigUInt.CreatePowerOfTwo(ctx.Workspace, ctx.QuotientBuffer.NumWords, 0);
        using var tempQuot = new GpuBigUInt(ctx.Workspace, ctx.QuotientBuffer.NumWords);
        using var tempRem = new GpuBigUInt(ctx.Workspace, ctx.EvalWords);

        var underflow = true;
        while (underflow)
        {
            loopCount++;
            if (loopCount > 10000)
            {
                var r = string.Join(",", ctx.RemainderExtended.ToHostArray());
                var b = string.Join(",", ctx.BPaddedExtended.ToHostArray());
                throw new Exception($"DivRem underflow correction failed to converge. evalWords: {ctx.EvalWords}, RemExtended: {r}, BPadded: {b}");
            }

            GpuBigIntMath.Sub(ctx.QuotientBuffer, one, tempQuot);
            ctx.QuotientBuffer.CopyFrom(tempQuot, numWords: ctx.QuotientBuffer.NumWords);

            GpuBigIntMath.Add(ctx.RemainderExtended, ctx.BPaddedExtended, tempRem);
            ctx.RemainderExtended.CopyFrom(tempRem, numWords: ctx.EvalWords);

            var topWord = ctx.RemainderExtended.GetTopWords(1)[0];
            underflow = (topWord & 0x80000000u) != 0;
        }
    }

    [ExcludeFromCodeCoverage]
    private static void CorrectRemainder(in BarrettCorrectionContext ctx)
    {
        var loopCount = 0;
        using var tempQuot = new GpuBigUInt(ctx.Workspace, ctx.QuotientBuffer.NumWords);
        using var tempRem = new GpuBigUInt(ctx.Workspace, ctx.EvalWords);
        using var shiftedB = new GpuBigUInt(ctx.Workspace, ctx.EvalWords);
        using var oneFallback = GpuBigUInt.CreatePowerOfTwo(ctx.Workspace, ctx.QuotientBuffer.NumWords, 0);

        while (!GpuBigIntMath.IsLessThan(ctx.RemainderExtended, ctx.BPaddedExtended))
        {
            loopCount++;
            if (loopCount > 10000)
            {
                throw new Exception("DivRem remainder correction failed to converge.");
            }

            var rBits = GetActualBits(ctx.RemainderExtended);
            var bBits = GetActualBits(ctx.BPaddedExtended);

            if (rBits > bBits + 1)
            {
                var shift = rBits - bBits - 1;
                GpuBigIntMath.ShiftLeft(ctx.BPaddedExtended, shift, shiftedB);

                if (GpuBigIntMath.IsLessThan(ctx.RemainderExtended, shiftedB))
                {
                    shift--;
                    GpuBigIntMath.ShiftLeft(ctx.BPaddedExtended, shift, shiftedB);
                }

                GpuBigIntMath.Sub(ctx.RemainderExtended, shiftedB, tempRem);
                ctx.RemainderExtended.CopyFrom(tempRem, numWords: ctx.EvalWords);

                using var correction = GpuBigUInt.CreatePowerOfTwo(ctx.Workspace, ctx.QuotientBuffer.NumWords, shift);
                GpuBigIntMath.Add(ctx.QuotientBuffer, correction, tempQuot);
                ctx.QuotientBuffer.CopyFrom(tempQuot, numWords: ctx.QuotientBuffer.NumWords);

                continue;
            }

            GpuBigIntMath.Sub(ctx.RemainderExtended, ctx.BPaddedExtended, tempRem);
            ctx.RemainderExtended.CopyFrom(tempRem, numWords: ctx.EvalWords);

            GpuBigIntMath.Add(ctx.QuotientBuffer, oneFallback, tempQuot);
            ctx.QuotientBuffer.CopyFrom(tempQuot, numWords: ctx.QuotientBuffer.NumWords);
        }
    }

    /// <summary>
    /// Calculates the exact number of significant bits in the specified <see cref="GpuBigUInt"/> by inspecting the highest non-zero bit.
    /// </summary>
    /// <param name="gpuBigInt">The big integer to inspect.</param>
    /// <returns>The bit length of the big integer, or 0 if the value is zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetActualBits(scoped GpuBigUIntView gpuBigInt) =>
        GetActualBitsCore(gpuBigInt.AsBorrowedBigUInt());

    // GetActualBits on the owned type; the public method above takes views and borrows them
    private static int GetActualBitsCore(GpuBigUInt gpuBigInt) => gpuBigInt.GetBitLength();

    /// <summary>
    /// Computes the arbitrary-precision integer quotient of two numbers on the GPU (<c>resultBuffer = leftOperand / rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The dividend operand.</param>
    /// <param name="rightOperand">The divisor operand.</param>
    /// <param name="resultBuffer">The destination buffer receiving the quotient. It may be either operand, or overlap
    /// them; the result is the one of separate buffers (see <see cref="DivRem"/>).</param>
    /// <exception cref="ArgumentException">Thrown when buffer sizes do not match.</exception>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="rightOperand"/> is zero.</exception>
    public static void Div(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView resultBuffer
    ) =>
        DivCore(leftOperand.AsBorrowedBigUInt(), rightOperand.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt());

    // Div on the owned type; the public method above takes views and borrows them
    private static void DivCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        using var tempR = new GpuBigUInt(leftOperand.Workspace, leftOperand.NumWords);
        DivRem(leftOperand, rightOperand, resultBuffer, tempR);
    }

    /// <summary>
    /// Computes the arbitrary-precision remainder (modulo) of two numbers on the GPU (<c>resultBuffer = leftOperand % rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The dividend operand.</param>
    /// <param name="rightOperand">The divisor operand.</param>
    /// <param name="resultBuffer">The destination buffer receiving the remainder. It may be either operand, or overlap
    /// them; the result is the one of separate buffers (see <see cref="DivRem"/>).</param>
    /// <exception cref="ArgumentException">Thrown when buffer sizes do not match.</exception>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="rightOperand"/> is zero.</exception>
    public static void Mod(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView resultBuffer
    ) =>
        ModCore(leftOperand.AsBorrowedBigUInt(), rightOperand.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt());

    // Mod on the owned type; the public method above takes views and borrows them
    private static void ModCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        using var tempQ = new GpuBigUInt(leftOperand.Workspace, leftOperand.NumWords);
        DivRem(leftOperand, rightOperand, tempQ, resultBuffer);
    }

    /// <summary>Extracts the most significant words (up to 4 limbs) as a <see cref="BigInteger"/> for initial Newton-Raphson seed estimation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BigInteger ExtractTopBitsAsBigInteger(GpuBigUInt operand, out int actualBits)
    {
        actualBits = operand.GetBitLength();
        if (actualBits == 0)
        {
            throw new DivideByZeroException();
        }

        var topIndex = (actualBits - 1) / 32;
        var wordsToExtract = Math.Min(4, topIndex + 1);
        var startWord = topIndex - wordsToExtract + 1;
        var hostData = ArrayPool<uint>.Shared.Rent(wordsToExtract);
        try
        {
            operand.Workspace.Provider.CopyDeviceToHost(
                hostData,
                IntPtr.Add(operand.DevicePointer, startWord * 4),
                wordsToExtract,
                operand.Workspace.Stream);
            return BigIntExtensions.ToBigInteger(hostData.AsSpan(0, wordsToExtract));
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostData);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ComputeInitialGuess(GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int targetBits, bool isSquareRoot)
    {
        var topBig = ExtractTopBitsAsBigInteger(targetOperand, out var actualBits);

        var truncatedBits = 0;
        var t = topBig;
        while (t > 0)
        {
            truncatedBits++;
            t >>= 1;
        }

        var shiftInTruncated = truncatedBits - 62;
        if (isSquareRoot && shiftInTruncated % 2 != 0)
        {
            shiftInTruncated++; // even if negative, maintain parity
        }

        var originalBigTop = shiftInTruncated >= 0
            ? (topBig >> shiftInTruncated)
            : (topBig << -shiftInTruncated);


        BigInteger initialApproximation;
        int finalShift;

        if (isSquareRoot)
        {
            var root = originalBigTop.Sqrt() + 1;
            initialApproximation = (BigInteger.One << 62) / root;
            var halfTotalShift = (actualBits - truncatedBits + shiftInTruncated) / 2;
            finalShift = targetBits - halfTotalShift - 62;
        }
        else
        {
            initialApproximation = (BigInteger.One << 124) / originalBigTop;
            finalShift = targetBits - actualBits - 62;
        }

        var shiftedApproximation = initialApproximation;
        if (finalShift >= 0)
        {
            shiftedApproximation <<= finalShift;
        }
        else
        {
            shiftedApproximation >>= -finalShift;
        }

        var numWords = resultBuffer.NumWords;
        var xData = ArrayPool<uint>.Shared.Rent(numWords);
        try
        {
            shiftedApproximation.WriteLimbs(xData.AsSpan(0, numWords));

            // No stream synchronization: a pageable host-to-device copy is staged before the call returns, so the pooled
            // host buffer may be returned immediately, and later work using resultBuffer is ordered on the same stream.
            targetOperand.Workspace.Provider.CopyHostToDevice(resultBuffer.DevicePointer, xData, numWords,
                targetOperand.Workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(xData);
        }
    }
}



