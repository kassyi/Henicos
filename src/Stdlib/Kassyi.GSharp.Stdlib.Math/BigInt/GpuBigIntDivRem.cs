using System.Buffers;
namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Provides static division and remainder operations, including Newton-Raphson approximation for <see cref="GpuUIntBigInteger"/>.
/// </summary>
public static class GpuBigIntDivRem
{
    private static void ThrowIfSizeMismatch(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        if (leftOperand.NumWords != rightOperand.NumWords || leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

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
    private static void Reciprocal(GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords = 0)
    {
        var n = targetOperand.NumWords;
        if (resultBuffer.NumWords != 2 * n + marginWords)
        {
            throw new ArgumentException("Result buffer for Reciprocal must be 2 * b.NumWords + marginWords.");
        }

        var numBits = n * 32;
        var marginBits = marginWords * 32;
        ComputeInitialGuess(targetOperand, resultBuffer, isSquareRoot: false, marginBits);

        using var constant = GpuUIntBigInteger.CreatePowerOfTwo(targetOperand.Context, 3 * n + marginWords, 2 * numBits + marginBits + 1);
        using var currentProduct = new GpuUIntBigInteger(targetOperand.Context, 3 * n + marginWords);
        using var currentDifference = new GpuUIntBigInteger(targetOperand.Context, 3 * n + marginWords);
        using var nextIterationValue = new GpuUIntBigInteger(targetOperand.Context, 5 * n + 2 * marginWords);

        var targetBits = 2 * numBits + marginBits;
        var currentBits = 63; // Initial guess has ~63 bits of precision
        ExecuteNewtonRaphsonIterations(currentBits, targetBits, () =>
        {
            var xiWords = (GetActualBits(resultBuffer) + 31) / 32 + 4; // Add 4 guard words for truncation margin
            if (xiWords > 2 * n + marginWords)
            {
                xiWords = 2 * n + marginWords;
            }

            if (xiWords == 0)
            {
                xiWords = 1;
            }

            using var xiTrunc = new GpuUIntBigInteger(targetOperand.Context, xiWords);
            xiTrunc.CopyFrom(resultBuffer, numWords: xiWords);

            var bTruncWords = System.Math.Min(n, xiWords);
            using var bTrunc = new GpuUIntBigInteger(targetOperand.Context, bTruncWords);
            bTrunc.CopyFrom(targetOperand, numWords: bTruncWords);

            var pWords = xiWords + bTruncWords;
            using var currentProductTrunc = new GpuUIntBigInteger(targetOperand.Context, pWords);
            GpuBigIntMath.Mul(bTrunc, xiTrunc, currentProductTrunc);

            // Compute currentDifference = constant - currentProduct
            // But we only care about the lower part!
            var constantWords = pWords;
            using var constantTrunc =
                GpuUIntBigInteger.CreatePowerOfTwo(targetOperand.Context, constantWords, 2 * numBits + marginBits + 1);
            using var diffTrunc = new GpuUIntBigInteger(targetOperand.Context, constantWords);
            GpuBigIntMath.Sub(constantTrunc, currentProductTrunc, diffTrunc);

            var diffActualWords = (GetActualBits(diffTrunc) + 31) / 32;
            if (diffActualWords == 0)
            {
                diffActualWords = 1;
            }

            using var diffActual = new GpuUIntBigInteger(targetOperand.Context, diffActualWords);
            diffActual.CopyFrom(diffTrunc, numWords: diffActualWords);

            var nextWords = xiWords + diffActualWords;
            using var nextIter = new GpuUIntBigInteger(targetOperand.Context, nextWords);
            GpuBigIntMath.Mul(xiTrunc, diffActual, nextIter);

            // Shift right by 2*numBits + marginBits
            resultBuffer.Clear();
            var copyWords = System.Math.Min(2 * n + marginWords, nextWords - (2 * n + marginWords));
            resultBuffer.CopyFrom(nextIter, destOffsetWords: 0, srcOffsetWords: 2 * n + marginWords, numWords: copyWords);
        });
    }

    private static void ExecuteNewtonRaphsonIterations(int currentBits, int targetBits, Action iterationBody)
    {
        var iterations = 0;
        while (currentBits < targetBits)
        {
            currentBits *= 2;
            iterations++;
        }

        iterations++; // One extra iteration for safety to ensure full convergence

        for (var i = 0; i < iterations; i++)
        {
            iterationBody();
        }
    }

    public static void DivRem(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger quotientBuffer, GpuUIntBigInteger remainderBuffer)
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

        quotientBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: 2 * n + marginWords, numWords: n);

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

    public static int GetActualBits(GpuUIntBigInteger gpuBigInt)
    {
        var n = gpuBigInt.NumWords;
        var hostData = ArrayPool<uint>.Shared.Rent(n);
        try
        {
            gpuBigInt.Context.Provider.CopyDeviceToHost(hostData, gpuBigInt.DevicePointer, n);
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
    public static void Div(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);

        var n = rightOperand.NumWords;

        var marginWords = 16;
        using var reciprocalOfB = new GpuUIntBigInteger(rightOperand.Context, 2 * n + marginWords);
        Reciprocal(rightOperand, reciprocalOfB, marginWords);

        using var productWithReciprocal = new GpuUIntBigInteger(rightOperand.Context, 3 * n + marginWords);
        GpuBigIntMath.Mul(leftOperand, reciprocalOfB, productWithReciprocal);

        resultBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: 2 * n + marginWords, numWords: n);
    }

    /// <summary>Computes arbitrary-precision modulo (<c>leftOperand % rightOperand</c>) on the GPU.</summary>
    public static void Mod(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        using var tempQ = new GpuUIntBigInteger(leftOperand.Context, leftOperand.NumWords);
        DivRem(leftOperand, rightOperand, tempQ, resultBuffer);
    }

    /// <summary>Computes modular exponentiation (<c>baseVal^exp mod mod</c>) on the GPU.</summary>
    public static void ModPow(GpuUIntBigInteger baseVal, GpuUIntBigInteger exp, GpuUIntBigInteger mod, GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(baseVal, mod, resultBuffer);

        var n = baseVal.NumWords;
        var expWords = exp.NumWords;

        using var mod2N = new GpuUIntBigInteger(baseVal.Context, 2 * n);
        mod2N.CopyFrom(mod, numWords: n);

        using var tempResult = new GpuUIntBigInteger(baseVal.Context, 2 * n);
        tempResult.Clear();
        tempResult.Context.Provider.CopyHostToDevice(tempResult.DevicePointer, [1u], 1);

        using var tempBase = new GpuUIntBigInteger(baseVal.Context, 2 * n);
        tempBase.CopyFrom(baseVal, numWords: n);

        // Initial reduction of base
        using var tempBaseQ = new GpuUIntBigInteger(baseVal.Context, 2 * n);
        using var tempBaseR = new GpuUIntBigInteger(baseVal.Context, 2 * n);
        DivRem(tempBase, mod2N, tempBaseQ, tempBaseR);
        tempBase.CopyFrom(tempBaseR);

        using var tempMul = new GpuUIntBigInteger(baseVal.Context, 4 * n);
        using var tempQ = new GpuUIntBigInteger(baseVal.Context, 4 * n);
        using var tempR = new GpuUIntBigInteger(baseVal.Context, 4 * n);

        using var mod4N = new GpuUIntBigInteger(baseVal.Context, 4 * n);
        mod4N.CopyFrom(mod, numWords: n);

        var hostExp = ArrayPool<uint>.Shared.Rent(expWords);
        try
        {
            exp.Context.Provider.CopyDeviceToHost(hostExp, exp.DevicePointer, expWords);

            for (var i = 0; i < expWords; i++)
            {
                var e = hostExp[i];

                // Optimization: if remaining bits are 0, we can break
                if (e == 0)
                {
                    var allZero = true;
                    for (var j = i; j < expWords; j++)
                    {
                        if (hostExp[j] == 0)
                        {
                            continue;
                        }

                        allZero = false;
                        break;
                    }

                    if (allZero)
                    {
                        break;
                    }
                }

                for (var bit = 0; bit < 32; bit++)
                {
                    if ((e & 1) == 1)
                    {
                        GpuBigIntMath.Mul(tempResult, tempBase, tempMul);
                        DivRem(tempMul, mod4N, tempQ, tempR);
                        tempResult.CopyFrom(tempR, numWords: 2 * n);
                    }

                    GpuBigIntMath.Mul(tempBase, tempBase, tempMul);
                    DivRem(tempMul, mod4N, tempQ, tempR);
                    tempBase.CopyFrom(tempR, numWords: 2 * n);

                    e >>= 1;

                    // If e becomes 0 and there are no more words, we can break bit loop
                    if (e == 0 && i == expWords - 1)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostExp);
        }

        resultBuffer.CopyFrom(tempResult, numWords: n);
    }

    /// <summary>
    /// Computes the inverse square root approximation of the operand using Newton-Raphson iterations on the GPU.
    /// X ≈ 2^B / sqrt(A)
    /// </summary>
    private static void InverseSqrt(GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords = 0)
    {
        var n = targetOperand.NumWords;
        if (resultBuffer.NumWords != n + marginWords)
        {
            throw new ArgumentException("Result buffer for InverseSqrt must be n + marginWords words.");
        }

        var marginBits = marginWords * 32;
        var actualBits = ComputeInitialGuess(targetOperand, resultBuffer, isSquareRoot: true, marginBits);

        // 3. GPU Newton-Raphson iterations
        var constant3Words = 3 * n + 2 * marginWords;
        using var constant3 = new GpuUIntBigInteger(targetOperand.Context, constant3Words);
        constant3.Clear();
        var hostConstant3 = ArrayPool<uint>.Shared.Rent(constant3Words);
        try
        {
            Array.Clear(hostConstant3, 0, constant3Words);
            var bit2B = 2 * actualBits + 2 * marginBits;
            hostConstant3[bit2B / 32] |= 1u << (bit2B % 32);
            hostConstant3[(bit2B + 1) / 32] |= 1u << ((bit2B + 1) % 32);
            targetOperand.Context.Provider.CopyHostToDevice(constant3.DevicePointer, hostConstant3, constant3Words);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostConstant3);
        }

        using var xSquared = new GpuUIntBigInteger(targetOperand.Context, 2 * n + 2 * marginWords);
        using var axSquared = new GpuUIntBigInteger(targetOperand.Context, 3 * n + 2 * marginWords);
        using var diff = new GpuUIntBigInteger(targetOperand.Context, 3 * n + 2 * marginWords);
        using var nextXUnshifted = new GpuUIntBigInteger(targetOperand.Context, 4 * n + 3 * marginWords);
        using var nextXShifted = new GpuUIntBigInteger(targetOperand.Context, 4 * n + 3 * marginWords);

        var targetBits = actualBits / 2 + marginBits;
        var currentBits = 31; // X_top has 31 bits
        ExecuteNewtonRaphsonIterations(currentBits, targetBits, () =>
        {
            GpuBigIntMath.Mul(resultBuffer, resultBuffer, xSquared);
            GpuBigIntMath.Mul(targetOperand, xSquared, axSquared);
            GpuBigIntMath.Sub(constant3, axSquared, diff);
            GpuBigIntMath.Mul(resultBuffer, diff, nextXUnshifted);

            // Shift right by 2S + 1
            nextXShifted.Clear();
            GpuBigIntMath.ShiftRight(nextXUnshifted, 2 * actualBits + 2 * marginBits + 1, nextXShifted);
            resultBuffer.CopyFrom(nextXShifted, destOffsetWords: 0, srcOffsetWords: 0, numWords: n + marginWords);
        });
    }

    private static BigInteger IntegerSqrt(BigInteger value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var guess = value >> (int)(value.GetBitLength() / 2);
        if (guess == 0)
        {
            guess = 1;
        }

        while (true)
        {
            var nextGuess = (guess + value / guess) >> 1;
            if (nextGuess >= guess)
            {
                return guess;
            }

            guess = nextGuess;
        }
    }

    /// <summary>Computes the integer square root (<c>floor(sqrt(leftOperand))</c>) on the GPU.</summary>
    public static void Sqrt(GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);

        var actualBits = GetActualBits(leftOperand);
        if (actualBits == 0)
        {
            resultBuffer.Clear();
            return;
        }

        if (TryComputeSingleWordSqrt(leftOperand, resultBuffer, actualBits))
        {
            return;
        }

        using var currentRoot = ComputeInitialSqrtGuess(leftOperand, actualBits);
        RefineSqrtWithHeronMethod(leftOperand, currentRoot, resultBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryComputeSingleWordSqrt(GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer, int actualBits)
    {
        if (actualBits > 32)
        {
            return false;
        }

        var n = leftOperand.NumWords;
        var hostDataSingle = ArrayPool<uint>.Shared.Rent(n);
        try
        {
            leftOperand.Context.Provider.CopyDeviceToHost(hostDataSingle, leftOperand.DevicePointer, n);
            var val = hostDataSingle[0];
            var sqrtVal = (uint)System.Math.Sqrt(val);
            resultBuffer.Clear();
            if (sqrtVal > 0)
            {
                resultBuffer.Context.Provider.CopyHostToDevice(resultBuffer.DevicePointer, [sqrtVal], 1);
            }
            return true;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostDataSingle);
        }
    }

    private static GpuUIntBigInteger ComputeInitialSqrtGuess(GpuUIntBigInteger leftOperand, int actualBits)
    {
        var n = leftOperand.NumWords;
        var lz = n * 32 - actualBits;
        if (lz % 2 != 0)
        {
            lz--;
        }

        using var aNorm = new GpuUIntBigInteger(leftOperand.Context, n);
        aNorm.Clear();
        GpuBigIntMath.ShiftLeft(leftOperand, lz, aNorm);

        var marginWords = 16;
        using var xResult = new GpuUIntBigInteger(leftOperand.Context, n + marginWords);
        xResult.Clear();
        InverseSqrt(aNorm, xResult, marginWords);

        using var rNorm = new GpuUIntBigInteger(leftOperand.Context, 2 * n + marginWords);
        GpuBigIntMath.Mul(aNorm, xResult, rNorm);

        var bBits = actualBits + lz;
        var marginBits = marginWords * 32;
        var totalShift = bBits + marginBits + (lz / 2);

        using var shiftedRNorm = new GpuUIntBigInteger(leftOperand.Context, 2 * n + marginWords);
        shiftedRNorm.Clear();
        GpuBigIntMath.ShiftRight(rNorm, totalShift, shiftedRNorm);

        var currentRoot = new GpuUIntBigInteger(leftOperand.Context, n);
        currentRoot.CopyFrom(shiftedRNorm, destOffsetWords: 0, srcOffsetWords: 0, numWords: n);
        
        if (GetActualBits(currentRoot) == 0)
        {
            currentRoot.Context.Provider.CopyHostToDevice(currentRoot.DevicePointer, [1u], 1);
        }

        return currentRoot;
    }

    private static void RefineSqrtWithHeronMethod(GpuUIntBigInteger leftOperand, GpuUIntBigInteger currentRoot, GpuUIntBigInteger resultBuffer)
    {
        var n = leftOperand.NumWords;
        using var divisionResult = new GpuUIntBigInteger(leftOperand.Context, n);
        using var sumWithDivision = new GpuUIntBigInteger(leftOperand.Context, n);
        using var prevRoot = new GpuUIntBigInteger(leftOperand.Context, n);

        while (true)
        {
            prevRoot.CopyFrom(currentRoot, numWords: n);

            Div(leftOperand, currentRoot, divisionResult);
            GpuBigIntMath.Add(currentRoot, divisionResult, sumWithDivision);
            currentRoot.Clear();
            GpuBigIntMath.ShiftRight(sumWithDivision, 1, currentRoot);

            using var diff = new GpuUIntBigInteger(leftOperand.Context, n);
            GpuBigIntMath.BitwiseXor(currentRoot, prevRoot, diff);
            if (GetActualBits(diff) == 0)
            {
                break;
            }
            
            if (GetActualBits(currentRoot) == 0)
            {
                break;
            }

            using var plusOne = new GpuUIntBigInteger(leftOperand.Context, n);
            plusOne.Context.Provider.CopyHostToDevice(plusOne.DevicePointer, [1u], 1);
            using var prevPlusOne = new GpuUIntBigInteger(leftOperand.Context, n);
            GpuBigIntMath.Add(prevRoot, plusOne, prevPlusOne);

            GpuBigIntMath.BitwiseXor(currentRoot, prevPlusOne, diff);
            if (GetActualBits(diff) == 0)
            {
                currentRoot.CopyFrom(prevRoot, numWords: n);
                break;
            }
        }

        using var squaredRoot = new GpuUIntBigInteger(leftOperand.Context, 2 * n);
        GpuBigIntMath.Mul(currentRoot, currentRoot, squaredRoot);

        var topHalf = new uint[n];
        leftOperand.Context.Provider.CopyDeviceToHost(topHalf, IntPtr.Add(squaredRoot.DevicePointer, n * 4), n);
        var isGreater = false;
        for (var i = 0; i < n; i++)
        {
            if (topHalf[i] == 0)
            {
                continue;
            }
            isGreater = true;
            break;
        }

        if (!isGreater)
        {
            using var lowerSquaredRoot = new GpuUIntBigInteger(leftOperand.Context, n);
            lowerSquaredRoot.CopyFrom(squaredRoot, numWords: n);
            isGreater = GpuBigIntMath.IsLessThan(leftOperand, lowerSquaredRoot);
        }

        if (isGreater)
        {
            using var one = new GpuUIntBigInteger(leftOperand.Context, n);
            one.Context.Provider.CopyHostToDevice(one.DevicePointer, [1u], 1);
            GpuBigIntMath.Sub(currentRoot, one, resultBuffer);
        }
        else
        {
            resultBuffer.CopyFrom(currentRoot);
        }
    }

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
            operand.Context.Provider.CopyDeviceToHost(hostData, operand.DevicePointer, n);
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

    private static int ComputeInitialGuess(GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, bool isSquareRoot, int marginBits = 0)
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

        var shift = actualBits - 62;
        if (shift < 0)
        {
            shift = 0;
        }

        if (isSquareRoot && shift % 2 != 0)
        {
            shift++;
        }

        BigInteger initialApproximation;
        int finalShift;
        var numBits = targetOperand.NumWords * 32;
        var n = targetOperand.NumWords;

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
            targetOperand.Context.Provider.CopyHostToDevice(resultBuffer.DevicePointer, xData, numWords);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(xData);
        }

        return actualBits;
    }
}



