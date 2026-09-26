using System.Buffers;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Provides static division and remainder operations, including Newton-Raphson approximation for <see cref="GpuUIntBigInteger"/>.
/// </summary>
public static partial class GpuBigIntDivRem
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowIfSizeMismatch(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        if (leftOperand.NumWords != rightOperand.NumWords || leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowIfSizeMismatch(GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer)
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
    internal static void Reciprocal(GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords = 0)
    {
        var n = targetOperand.NumWords;
        if (resultBuffer.NumWords != 2 * n + marginWords)
        {
            throw new ArgumentException("Result buffer for Reciprocal must be 2 * b.NumWords + marginWords.");
        }

        var numBits = n * 32;
        var marginBits = marginWords * 32;
        ComputeInitialGuess(targetOperand, resultBuffer, isSquareRoot: false, marginBits);

        var targetBits = 2 * numBits + marginBits;
        var currentBits = 63; // Initial guess has ~63 bits of precision
        var iterations = CalculateNewtonRaphsonIterationCount(currentBits, targetBits);

        if (targetOperand.Context.Provider is { } provider)
        {
            provider.ReciprocalMega(targetOperand.Context, targetOperand, resultBuffer, marginWords, numBits,
                marginBits, iterations);
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

    public static void DivRem(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger quotientBuffer, GpuUIntBigInteger remainderBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, quotientBuffer);
        ThrowIfSizeMismatch(leftOperand, remainderBuffer);

        var n = rightOperand.NumWords;
        var bActualBits = GetActualBits(rightOperand);
        if (bActualBits == 0)
        {
            throw new DivideByZeroException();
        }

        var lz = n * 32 - bActualBits;

        using var bNorm = new GpuUIntBigInteger(rightOperand.Context, n);
        GpuBigIntMath.ShiftLeft(rightOperand, lz, bNorm);

        var marginWords = 16;
        using var reciprocalNorm = new GpuUIntBigInteger(rightOperand.Context, 2 * n + marginWords);
        reciprocalNorm.Clear();
        Reciprocal(bNorm, reciprocalNorm, marginWords);

        using var reciprocalOfB = new GpuUIntBigInteger(rightOperand.Context, 2 * n + marginWords);
        GpuBigIntMath.ShiftLeft(reciprocalNorm, lz, reciprocalOfB);

        using var productWithReciprocal = new GpuUIntBigInteger(rightOperand.Context, 3 * n + marginWords);
        GpuBigIntMath.Mul(leftOperand, reciprocalOfB, productWithReciprocal);

        quotientBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: 2 * n + marginWords,
            numWords: n);

        using var qb = new GpuUIntBigInteger(rightOperand.Context, 2 * n);
        GpuBigIntMath.Mul(quotientBuffer, rightOperand, qb);

        var aActualWords = (GetActualBits(leftOperand) + 31) / 32;
        var qbActualWords = (GetActualBits(qb) + 31) / 32;
        var evalWords = System.Math.Max(aActualWords, qbActualWords) + 1; // +1 to allow borrow logic

        using var qbTrunc = new GpuUIntBigInteger(rightOperand.Context, evalWords);
        qbTrunc.CopyFrom(qb, numWords: System.Math.Min(2 * n, evalWords));

        using var leftOperandExtended = new GpuUIntBigInteger(rightOperand.Context, evalWords);
        leftOperandExtended.Clear();
        leftOperandExtended.CopyFrom(leftOperand, numWords: System.Math.Min(n, evalWords));

        using var remainderExtended = new GpuUIntBigInteger(rightOperand.Context, evalWords);
        GpuBigIntMath.Sub(leftOperandExtended, qbTrunc, remainderExtended);

        var topWord = remainderExtended.GetTopWords(1)[0];
        var underflow = (topWord & 0x80000000u) != 0;

        if (underflow)
        {
            using var bPaddedExtended = new GpuUIntBigInteger(rightOperand.Context, evalWords);
            bPaddedExtended.Clear();
            bPaddedExtended.CopyFrom(rightOperand, numWords: System.Math.Min(n, evalWords));

            CorrectUnderflow(rightOperand.Context, n, evalWords, quotientBuffer, remainderExtended, bPaddedExtended);
        }
        else
        {
            using var bPaddedExtended = new GpuUIntBigInteger(rightOperand.Context, evalWords);
            bPaddedExtended.Clear();
            bPaddedExtended.CopyFrom(rightOperand, numWords: System.Math.Min(n, evalWords));

            CorrectRemainder(rightOperand.Context, n, evalWords, quotientBuffer, remainderExtended, bPaddedExtended);
        }

        remainderBuffer.Clear();
        remainderBuffer.CopyFrom(remainderExtended, numWords: System.Math.Min(n, evalWords));
    }

    private static void CorrectUnderflow(GpuContext context, int n, int evalWords, GpuUIntBigInteger quotientBuffer,
        GpuUIntBigInteger remainderExtended, GpuUIntBigInteger bPaddedExtended)
    {
        var loopCount = 0;
        using var one = GpuUIntBigInteger.CreatePowerOfTwo(context, n, 0);
        using var tempQuot = new GpuUIntBigInteger(context, n);
        using var tempRem = new GpuUIntBigInteger(context, evalWords);

        var underflow = true;
        while (underflow)
        {
            loopCount++;
            if (loopCount > 10000)
            {
                throw new Exception("DivRem underflow correction failed to converge.");
            }

            GpuBigIntMath.Sub(quotientBuffer, one, tempQuot);
            quotientBuffer.CopyFrom(tempQuot, numWords: n);

            GpuBigIntMath.Add(remainderExtended, bPaddedExtended, tempRem);
            remainderExtended.CopyFrom(tempRem, numWords: evalWords);

            var topWord = remainderExtended.GetTopWords(1)[0];
            underflow = (topWord & 0x80000000u) != 0;
        }
    }

    private static void CorrectRemainder(GpuContext context, int n, int evalWords, GpuUIntBigInteger quotientBuffer,
        GpuUIntBigInteger remainderExtended, GpuUIntBigInteger bPaddedExtended)
    {
        var loopCount = 0;
        using var tempQuot = new GpuUIntBigInteger(context, n);
        using var tempRem = new GpuUIntBigInteger(context, evalWords);
        using var shiftedB = new GpuUIntBigInteger(context, evalWords);
        using var oneFallback = GpuUIntBigInteger.CreatePowerOfTwo(context, n, 0);

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

                using var correction = GpuUIntBigInteger.CreatePowerOfTwo(context, n, shift);
                GpuBigIntMath.Add(quotientBuffer, correction, tempQuot);
                quotientBuffer.CopyFrom(tempQuot, numWords: n);

                continue;
            }

            GpuBigIntMath.Sub(remainderExtended, bPaddedExtended, tempRem);
            remainderExtended.CopyFrom(tempRem, numWords: evalWords);

            GpuBigIntMath.Add(quotientBuffer, oneFallback, tempQuot);
            quotientBuffer.CopyFrom(tempQuot, numWords: n);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetActualBits(GpuUIntBigInteger gpuBigInt)
    {
        var n = gpuBigInt.NumWords;
        var hostData = ArrayPool<uint>.Shared.Rent(n);
        try
        {
            gpuBigInt.Context.Provider.CopyDeviceToHost(hostData, gpuBigInt.DevicePointer, n, gpuBigInt.Context.Stream);
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

    /// <summary>Computes arbitrary-precision division (<c>leftOperand / rightOperand</c>) on the GPU.</summary>
    public static void Div(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);

        var n = rightOperand.NumWords;

        var marginWords = 16;
        using var reciprocalOfB = new GpuUIntBigInteger(rightOperand.Context, 2 * n + marginWords);
        Reciprocal(rightOperand, reciprocalOfB, marginWords);

        using var productWithReciprocal = new GpuUIntBigInteger(rightOperand.Context, 3 * n + marginWords);
        GpuBigIntMath.Mul(leftOperand, reciprocalOfB, productWithReciprocal);

        resultBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: 2 * n + marginWords,
            numWords: n);
    }

    /// <summary>Computes arbitrary-precision modulo (<c>leftOperand % rightOperand</c>) on the GPU.</summary>
    public static void Mod(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        using var tempQ = new GpuUIntBigInteger(leftOperand.Context, leftOperand.NumWords);
        DivRem(leftOperand, rightOperand, tempQ, resultBuffer);
    }

    /// <summary>Computes modular exponentiation (<c>baseVal^exp mod mod</c>) on the GPU.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BigInteger ExtractTopBitsAsBigInteger(GpuUIntBigInteger operand, out int actualBits)
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
            operand.Context.Provider.CopyDeviceToHost(hostData, operand.DevicePointer, n, operand.Context.Stream);
            var topIndex = (actualBits - 1) / 32;

            var wordsToExtract = System.Math.Min(4, topIndex + 1);
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
    private static void ComputeInitialGuess(GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer,
        bool isSquareRoot, int marginBits = 0)
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
        var numBits = targetOperand.NumWords * 32;

        if (isSquareRoot)
        {
            var root = IntegerSqrt(originalBigTop);
            initialApproximation = (BigInteger.One << 62) / root;
            finalShift = actualBits / 2 + marginBits - 31;
        }
        else
        {
            initialApproximation = (BigInteger.One << 124) / originalBigTop;
            finalShift = 2 * numBits + marginBits - actualBits - 62;
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
            Array.Clear(xData, 0, numWords);
            var xBytes = shiftedApproximation.ToByteArray(isUnsigned: true, isBigEndian: false);
            Buffer.BlockCopy(xBytes, 0, xData, 0, System.Math.Min(xBytes.Length, numWords * 4));
            targetOperand.Context.Provider.CopyHostToDevice(resultBuffer.DevicePointer, xData, numWords, targetOperand.Context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(xData);
        }
    }
}



