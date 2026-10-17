
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

    /// <summary>
    /// Computes the reciprocal approximation of the operand using Newton-Raphson iterations on the GPU.
    /// </summary>
    internal static void Reciprocal(GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int targetBits,
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
    /// <param name="quotientBuffer">The destination buffer receiving the quotient ($\lfloor A / B \rfloor$).</param>
    /// <param name="remainderBuffer">The destination buffer receiving the remainder ($A \bmod B$).</param>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="rightOperand"/> is zero.</exception>
    /// <remarks>
    /// <para><b>Algorithm:</b> Evaluates division via normalized reciprocal multiplication on GPU followed by quotient-remainder correction.</para>
    /// </remarks>
    public static void DivRem(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
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
        if (aActualBits < bActualBits)
        {
            quotientBuffer.Clear();
            remainderBuffer.CopyFrom(leftOperand, numWords: Math.Min(remainderBuffer.NumWords, nA));
            return;
        }

        var lz = n * 32 - bActualBits;

        using var bNorm = new GpuBigUInt(rightOperand.Workspace, n);
        GpuBigIntMath.ShiftLeft(rightOperand, lz, bNorm);

        var marginBits = 512;
        var marginWords = marginBits / 32;
        var aActualWords = (aActualBits + 31) / 32;
        var targetBits = aActualWords * 32 + marginBits;
        var targetWords = (targetBits + 31) / 32;

        using var reciprocalNorm = new GpuBigUInt(rightOperand.Workspace, targetWords);
        reciprocalNorm.Clear();
        Reciprocal(bNorm, reciprocalNorm, targetBits, marginWords);

        using var reciprocalOfB = new GpuBigUInt(rightOperand.Workspace, targetWords);
        GpuBigIntMath.ShiftLeft(reciprocalNorm, lz, reciprocalOfB);

        Reduce(leftOperand, rightOperand, reciprocalOfB, targetBits, aActualWords, quotientBuffer, remainderBuffer);
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
    public static void Reduce(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt reciprocalOfB,
        int targetBits, int aActualWords, GpuBigUInt quotientBuffer, GpuBigUInt remainderBuffer)
    {
        var nA = leftOperand.NumWords;
        var n = rightOperand.NumWords;
        var targetWords = reciprocalOfB.NumWords;

        using var productWithReciprocal = new GpuBigUInt(rightOperand.Workspace, nA + targetWords);
        GpuBigIntMath.Mul(leftOperand, reciprocalOfB, productWithReciprocal);

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
            bPaddedExtended.Clear(); // intentional double clear from original code just in case
            bPaddedExtended.CopyFrom(rightOperand, numWords: Math.Min(n, evalWords));

            CorrectUnderflow(rightOperand.Workspace, evalWords, quotientBuffer, remainderExtended, bPaddedExtended);
        }
        else
        {
            using var bPaddedExtended = new GpuBigUInt(rightOperand.Workspace, evalWords);
            bPaddedExtended.Clear();
            bPaddedExtended.CopyFrom(rightOperand, numWords: Math.Min(rightOperand.NumWords, evalWords));

            CorrectRemainder(rightOperand.Workspace, evalWords, quotientBuffer, remainderExtended, bPaddedExtended);
        }

        remainderBuffer.Clear();
        remainderBuffer.CopyFrom(remainderExtended, numWords: Math.Min(remainderBuffer.NumWords, evalWords));
    }

    /// <summary>
    /// Performs a zero-sync Barrett reduction using a precomputed reciprocal.
    /// This method does not synchronize with the CPU for remainder correction, enabling CUDA Graph capture.
    /// </summary>
    /// <param name="leftOperand">The left operand.</param>
    /// <param name="rightOperand">The right operand.</param>
    /// <param name="reciprocalOfB">The precomputed reciprocal of the right operand.</param>
    /// <param name="targetBits">The target precision in bits.</param>
    /// <param name="aActualWords">The actual word count of the left operand.</param>
    /// <param name="quotientBuffer">The buffer to store the quotient result.</param>
    /// <param name="remainderBuffer">The buffer to store the remainder result.</param>
    public static void ReduceZeroSync(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt reciprocalOfB,
        int targetBits, int aActualWords, GpuBigUInt quotientBuffer, GpuBigUInt remainderBuffer)
    {
        var nA = leftOperand.NumWords;
        var n = rightOperand.NumWords;
        var targetWords = reciprocalOfB.NumWords;

        using var productWithReciprocal = new GpuBigUInt(rightOperand.Workspace, nA + targetWords);
        GpuBigIntMath.Mul(leftOperand, reciprocalOfB, productWithReciprocal);

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

        // Zero-sync correction using single block kernel
        GpuBigIntDivRemKernels.LaunchBarrettCorrectionKernelPtr(
            remainderExtended.DevicePointer, bPaddedExtended.DevicePointer, evalWords,
            gridDimX: 1, blockDimX: 1024, hStream: rightOperand.Workspace.Stream);

        remainderBuffer.Clear();
        remainderBuffer.CopyFrom(remainderExtended, numWords: Math.Min(remainderBuffer.NumWords, evalWords));
    }


    [ExcludeFromCodeCoverage]
    private static void CorrectUnderflow(GpuWorkspace workspace, int evalWords, GpuBigUInt quotientBuffer,
        GpuBigUInt remainderExtended, GpuBigUInt bPaddedExtended)
    {
        var loopCount = 0;
        using var one = GpuBigUInt.CreatePowerOfTwo(workspace, quotientBuffer.NumWords, 0);
        using var tempQuot = new GpuBigUInt(workspace, quotientBuffer.NumWords);
        using var tempRem = new GpuBigUInt(workspace, evalWords);

        var underflow = true;
        while (underflow)
        {
            loopCount++;
            if (loopCount > 10000)
            {
                var r = string.Join(",", remainderExtended.ToHostArray());
                var b = string.Join(",", bPaddedExtended.ToHostArray());
                throw new Exception($"DivRem underflow correction failed to converge. evalWords: {evalWords}, RemExtended: {r}, BPadded: {b}");
            }

            GpuBigIntMath.Sub(quotientBuffer, one, tempQuot);
            quotientBuffer.CopyFrom(tempQuot, numWords: quotientBuffer.NumWords);

            GpuBigIntMath.Add(remainderExtended, bPaddedExtended, tempRem);
            remainderExtended.CopyFrom(tempRem, numWords: evalWords);

            var topWord = remainderExtended.GetTopWords(1)[0];
            underflow = (topWord & 0x80000000u) != 0;
        }
    }

    [ExcludeFromCodeCoverage]
    private static void CorrectRemainder(GpuWorkspace workspace, int evalWords, GpuBigUInt quotientBuffer,
        GpuBigUInt remainderExtended, GpuBigUInt bPaddedExtended)
    {
        var loopCount = 0;
        using var tempQuot = new GpuBigUInt(workspace, quotientBuffer.NumWords);
        using var tempRem = new GpuBigUInt(workspace, evalWords);
        using var shiftedB = new GpuBigUInt(workspace, evalWords);
        using var oneFallback = GpuBigUInt.CreatePowerOfTwo(workspace, quotientBuffer.NumWords, 0);

        while (!GpuBigIntMath.IsLessThan(remainderExtended, bPaddedExtended))
        {
            loopCount++;
            if (loopCount > 10000)
            {
                throw new Exception("DivRem remainder correction failed to converge.");
            }

            var rBits = GetActualBits(remainderExtended);
            var bBits = GetActualBits(bPaddedExtended);

            if (rBits > bBits + 1)
            {
                var shift = rBits - bBits - 1;
                GpuBigIntMath.ShiftLeft(bPaddedExtended, shift, shiftedB);

                if (GpuBigIntMath.IsLessThan(remainderExtended, shiftedB))
                {
                    shift--;
                    GpuBigIntMath.ShiftLeft(bPaddedExtended, shift, shiftedB);
                }

                GpuBigIntMath.Sub(remainderExtended, shiftedB, tempRem);
                remainderExtended.CopyFrom(tempRem, numWords: evalWords);

                using var correction = GpuBigUInt.CreatePowerOfTwo(workspace, quotientBuffer.NumWords, shift);
                GpuBigIntMath.Add(quotientBuffer, correction, tempQuot);
                quotientBuffer.CopyFrom(tempQuot, numWords: quotientBuffer.NumWords);

                continue;
            }

            GpuBigIntMath.Sub(remainderExtended, bPaddedExtended, tempRem);
            remainderExtended.CopyFrom(tempRem, numWords: evalWords);

            GpuBigIntMath.Add(quotientBuffer, oneFallback, tempQuot);
            quotientBuffer.CopyFrom(tempQuot, numWords: quotientBuffer.NumWords);
        }
    }

    /// <summary>
    /// Calculates the exact number of significant bits in the specified <see cref="GpuBigUInt"/> by inspecting the highest non-zero bit.
    /// </summary>
    /// <param name="gpuBigInt">The big integer to inspect.</param>
    /// <returns>The bit length of the big integer, or 0 if the value is zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetActualBits(GpuBigUInt gpuBigInt)
    {
        var n = gpuBigInt.NumWords;
        var hostData = ArrayPool<uint>.Shared.Rent(n);
        try
        {
            gpuBigInt.Workspace.Provider.CopyDeviceToHost(hostData, gpuBigInt.DevicePointer, n, gpuBigInt.Workspace.Stream);
            for (var i = n - 1; i >= 0; i--)
            {
                if (hostData[i] == 0)
                {
                    continue;
                }

                var w = hostData[i];
                var b = 31;
                while ((w & (1u << b)) == 0)
                {
                    b--;
                }

                return i * 32 + b + 1;
            }

            return 0;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostData);
        }
    }

    /// <summary>
    /// Computes the arbitrary-precision integer quotient of two numbers on the GPU (<c>resultBuffer = leftOperand / rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The dividend operand.</param>
    /// <param name="rightOperand">The divisor operand.</param>
    /// <param name="resultBuffer">The destination buffer receiving the quotient.</param>
    /// <exception cref="ArgumentException">Thrown when buffer sizes do not match.</exception>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="rightOperand"/> is zero.</exception>
    public static void Div(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
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
    /// <param name="resultBuffer">The destination buffer receiving the remainder.</param>
    /// <exception cref="ArgumentException">Thrown when buffer sizes do not match.</exception>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="rightOperand"/> is zero.</exception>
    public static void Mod(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        using var tempQ = new GpuBigUInt(leftOperand.Workspace, leftOperand.NumWords);
        DivRem(leftOperand, rightOperand, tempQ, resultBuffer);
    }

    /// <summary>Computes modular exponentiation (<c>baseVal^exp mod mod</c>) on the GPU.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BigInteger ExtractTopBitsAsBigInteger(GpuBigUInt operand, out int actualBits)
    {
        actualBits = GetActualBits(operand);
        if (actualBits == 0)
        {
            throw new DivideByZeroException();
        }

        var n = operand.NumWords;
        var hostData = ArrayPool<uint>.Shared.Rent(n);
        try
        {
            operand.Workspace.Provider.CopyDeviceToHost(hostData, operand.DevicePointer, n, operand.Workspace.Stream);
            var topIndex = (actualBits - 1) / 32;

            var wordsToExtract = Math.Min(4, topIndex + 1);
            var extractBytes = new byte[wordsToExtract * 4 + 1];
            Buffer.BlockCopy(hostData, (topIndex - wordsToExtract + 1) * 4, extractBytes, 0, wordsToExtract * 4);
            return new BigInteger(extractBytes);
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



