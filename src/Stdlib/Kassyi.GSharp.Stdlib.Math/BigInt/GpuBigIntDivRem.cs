namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Provides static division and remainder operations, including Newton-Raphson approximation for <see cref="GpuBigInteger"/>.
/// </summary>
public static class GpuBigIntDivRem
{
    private static void ThrowIfSizeMismatch(GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        if (leftOperand.NumWords != rightOperand.NumWords || leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    private static void ThrowIfSizeMismatch(GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
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
    private static void Reciprocal(GpuBigInteger targetOperand, GpuBigInteger resultBuffer)
    {
        var n = targetOperand.NumWords;
        if (resultBuffer.NumWords != 2 * n)
        {
            throw new ArgumentException("Result buffer for Reciprocal must be 2 * b.NumWords.");
        }

        var numBits = n * 32;
        ComputeInitialGuess(targetOperand, resultBuffer, isSquareRoot: false);

        using var constant = GpuBigInteger.CreatePowerOfTwo(targetOperand.Context, 3 * n, 2 * numBits + 1);
        using var currentProduct = new GpuBigInteger(targetOperand.Context, 3 * n);
        using var currentDifference = new GpuBigInteger(targetOperand.Context, 3 * n);
        using var nextIterationValue = new GpuBigInteger(targetOperand.Context, 5 * n);

        var targetBits = 2 * numBits;
        var currentBits = 124;
        var iterations = 0;
        while (currentBits < targetBits)
        {
            currentBits *= 2;
            iterations++;
        }

        iterations++; // One extra iteration for safety to ensure full convergence

        for (var i = 0; i < iterations; i++)
        {
            var xiWords = (GetActualBits(resultBuffer) + 31) / 32;
            if (xiWords == 0)
            {
                xiWords = 1;
            }

            using var xiTrunc = new GpuBigInteger(targetOperand.Context, xiWords);
            xiTrunc.CopyFrom(resultBuffer, numWords: xiWords);

            var bTruncWords = System.Math.Min(n, xiWords);
            using var bTrunc = new GpuBigInteger(targetOperand.Context, bTruncWords);
            bTrunc.CopyFrom(targetOperand, numWords: bTruncWords);

            var pWords = xiWords + bTruncWords;
            using var currentProductTrunc = new GpuBigInteger(targetOperand.Context, pWords);
            GpuBigIntMath.Mul(bTrunc, xiTrunc, currentProductTrunc);

            // Compute currentDifference = constant - currentProduct
            // But we only care about the lower part!
            var constantWords = pWords;
            using var constantTrunc =
                GpuBigInteger.CreatePowerOfTwo(targetOperand.Context, constantWords, 2 * numBits + 1);
            using var diffTrunc = new GpuBigInteger(targetOperand.Context, constantWords);
            GpuBigIntMath.Sub(constantTrunc, currentProductTrunc, diffTrunc);

            var diffActualWords = (GetActualBits(diffTrunc) + 31) / 32;
            if (diffActualWords == 0)
            {
                diffActualWords = 1;
            }

            using var diffActual = new GpuBigInteger(targetOperand.Context, diffActualWords);
            diffActual.CopyFrom(diffTrunc, numWords: diffActualWords);

            var nextWords = xiWords + diffActualWords;
            using var nextIter = new GpuBigInteger(targetOperand.Context, nextWords);
            GpuBigIntMath.Mul(xiTrunc, diffActual, nextIter);

            resultBuffer.Clear();
            if (nextWords <= 2 * n)
            {
                continue;
            }

            var copyWords = System.Math.Min(2 * n, nextWords - 2 * n);
            resultBuffer.CopyFrom(nextIter, destOffsetWords: 0, srcOffsetWords: 2 * n, numWords: copyWords);
        }
    }

    public static void DivRem(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger quotientBuffer,
        GpuBigInteger remainderBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, quotientBuffer);
        ThrowIfSizeMismatch(leftOperand, remainderBuffer);

        var n = rightOperand.NumWords;
        var bActualBits = GetActualBits(rightOperand);
        var lz = n * 32 - bActualBits;

        using var bNorm = new GpuBigInteger(rightOperand.Context, n);
        bNorm.Clear();
        GpuBigIntMath.ShiftLeft(rightOperand, lz, bNorm);

        using var reciprocalNorm = new GpuBigInteger(rightOperand.Context, 2 * n);
        reciprocalNorm.Clear();
        Reciprocal(bNorm, reciprocalNorm);

        using var reciprocalOfB = new GpuBigInteger(rightOperand.Context, 2 * n);
        reciprocalOfB.Clear();
        GpuBigIntMath.ShiftLeft(reciprocalNorm, lz, reciprocalOfB);

        using var productWithReciprocal = new GpuBigInteger(rightOperand.Context, 3 * n);
        GpuBigIntMath.Mul(leftOperand, reciprocalOfB, productWithReciprocal);

        quotientBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: 2 * n, numWords: n);

        using var qb = new GpuBigInteger(rightOperand.Context, 2 * n);
        GpuBigIntMath.Mul(quotientBuffer, rightOperand, qb);

        var aActualWords = (GetActualBits(leftOperand) + 31) / 32;
        var qbActualWords = (GetActualBits(qb) + 31) / 32;
        var evalWords = System.Math.Max(aActualWords, qbActualWords) + 1; // +1 to allow borrow logic

        using var qbTrunc = new GpuBigInteger(rightOperand.Context, evalWords);
        qbTrunc.CopyFrom(qb, numWords: System.Math.Min(2 * n, evalWords));

        using var leftOperandExtended = new GpuBigInteger(rightOperand.Context, evalWords);
        leftOperandExtended.Clear();
        leftOperandExtended.CopyFrom(leftOperand, numWords: System.Math.Min(n, evalWords));

        using var remainderExtended = new GpuBigInteger(rightOperand.Context, evalWords);
        GpuBigIntMath.Sub(leftOperandExtended, qbTrunc, remainderExtended);

        var topWord = remainderExtended.GetTopWords(1)[0];
        var underflow = (topWord & 0x80000000u) != 0;

        if (underflow)
        {
            using var bPaddedExtended = new GpuBigInteger(rightOperand.Context, evalWords);
            bPaddedExtended.Clear();
            bPaddedExtended.CopyFrom(rightOperand, numWords: System.Math.Min(n, evalWords));

            CorrectUnderflow(rightOperand.Context, n, evalWords, quotientBuffer, remainderExtended, bPaddedExtended);
        }
        else
        {
            using var bPaddedExtended = new GpuBigInteger(rightOperand.Context, evalWords);
            bPaddedExtended.Clear();
            bPaddedExtended.CopyFrom(rightOperand, numWords: System.Math.Min(n, evalWords));

            CorrectRemainder(rightOperand.Context, n, evalWords, quotientBuffer, remainderExtended, bPaddedExtended);
        }

        remainderBuffer.Clear();
        remainderBuffer.CopyFrom(remainderExtended, numWords: System.Math.Min(n, evalWords));
    }

    private static void CorrectUnderflow(GpuContext context, int n, int evalWords, GpuBigInteger quotientBuffer,
        GpuBigInteger remainderExtended, GpuBigInteger bPaddedExtended)
    {
        var loopCount = 0;
        using var one = GpuBigInteger.CreatePowerOfTwo(context, n, 0);
        using var tempQuot = new GpuBigInteger(context, n);
        using var tempRem = new GpuBigInteger(context, evalWords);

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

    private static void CorrectRemainder(GpuContext context, int n, int evalWords, GpuBigInteger quotientBuffer,
        GpuBigInteger remainderExtended, GpuBigInteger bPaddedExtended)
    {
        var loopCount = 0;
        using var tempQuot = new GpuBigInteger(context, n);
        using var tempRem = new GpuBigInteger(context, evalWords);
        using var shiftedB = new GpuBigInteger(context, evalWords);
        using var oneFallback = GpuBigInteger.CreatePowerOfTwo(context, n, 0);

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
                if (shift > 0)
                {
                    shiftedB.Clear();
                    GpuBigIntMath.ShiftLeft(bPaddedExtended, shift, shiftedB);

                    if (GpuBigIntMath.IsLessThan(remainderExtended, shiftedB))
                    {
                        shift--;
                        if (shift > 0)
                        {
                            shiftedB.Clear();
                            GpuBigIntMath.ShiftLeft(bPaddedExtended, shift, shiftedB);
                            GpuBigIntMath.Sub(remainderExtended, shiftedB, tempRem);
                            remainderExtended.CopyFrom(tempRem, numWords: evalWords);

                            using var correction = GpuBigInteger.CreatePowerOfTwo(context, n, shift);
                            GpuBigIntMath.Add(quotientBuffer, correction, tempQuot);
                            quotientBuffer.CopyFrom(tempQuot, numWords: n);
                        }
                        else
                        {
                            GpuBigIntMath.Sub(remainderExtended, bPaddedExtended, tempRem);
                            remainderExtended.CopyFrom(tempRem, numWords: evalWords);

                            GpuBigIntMath.Add(quotientBuffer, oneFallback, tempQuot);
                            quotientBuffer.CopyFrom(tempQuot, numWords: n);
                        }
                    }
                    else
                    {
                        GpuBigIntMath.Sub(remainderExtended, shiftedB, tempRem);
                        remainderExtended.CopyFrom(tempRem, numWords: evalWords);

                        using var correction = GpuBigInteger.CreatePowerOfTwo(context, n, shift);
                        GpuBigIntMath.Add(quotientBuffer, correction, tempQuot);
                        quotientBuffer.CopyFrom(tempQuot, numWords: n);
                    }

                    continue;
                }
            }

            GpuBigIntMath.Sub(remainderExtended, bPaddedExtended, tempRem);
            remainderExtended.CopyFrom(tempRem, numWords: evalWords);

            GpuBigIntMath.Add(quotientBuffer, oneFallback, tempQuot);
            quotientBuffer.CopyFrom(tempQuot, numWords: n);
        }
    }

    public static int GetActualBits(GpuBigInteger gpuBigInt)
    {
        var hostData = gpuBigInt.ToHostArray();
        for (var i = hostData.Length - 1; i >= 0; i--)
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

    /// <summary>Computes arbitrary-precision division (<c>leftOperand / rightOperand</c>) on the GPU.</summary>
    public static void Div(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);

        var n = rightOperand.NumWords;

        using var reciprocalOfB = new GpuBigInteger(rightOperand.Context, 2 * n);
        Reciprocal(rightOperand, reciprocalOfB);

        using var productWithReciprocal = new GpuBigInteger(rightOperand.Context, 3 * n);
        GpuBigIntMath.Mul(leftOperand, reciprocalOfB, productWithReciprocal);
        resultBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: 2 * n, numWords: n);
    }

    /// <summary>Computes arbitrary-precision modulo (<c>leftOperand % rightOperand</c>) on the GPU.</summary>
    public static void Mod(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        using var tempQ = new GpuBigInteger(leftOperand.Context, leftOperand.NumWords);
        DivRem(leftOperand, rightOperand, tempQ, resultBuffer);
    }

    /// <summary>Computes modular exponentiation (<c>baseVal^exp mod mod</c>) on the GPU.</summary>
    public static void ModPow(GpuBigInteger baseVal, GpuBigInteger exp, GpuBigInteger mod, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(baseVal, mod, resultBuffer);

        var n = baseVal.NumWords;
        var expWords = exp.NumWords;

        using var mod2N = new GpuBigInteger(baseVal.Context, 2 * n);
        mod2N.CopyFrom(mod, numWords: n);

        using var tempResult = new GpuBigInteger(baseVal.Context, 2 * n);
        var oneData = new uint[2 * n];
        oneData[0] = 1;
        tempResult.CopyFrom(new GpuBigInteger(baseVal.Context, oneData));

        using var tempBase = new GpuBigInteger(baseVal.Context, 2 * n);
        tempBase.CopyFrom(baseVal, numWords: n);

        // Initial reduction of base
        using var tempBaseQ = new GpuBigInteger(baseVal.Context, 2 * n);
        using var tempBaseR = new GpuBigInteger(baseVal.Context, 2 * n);
        DivRem(tempBase, mod2N, tempBaseQ, tempBaseR);
        tempBase.CopyFrom(tempBaseR);

        using var tempMul = new GpuBigInteger(baseVal.Context, 4 * n);
        using var tempQ = new GpuBigInteger(baseVal.Context, 4 * n);
        using var tempR = new GpuBigInteger(baseVal.Context, 4 * n);

        using var mod4N = new GpuBigInteger(baseVal.Context, 4 * n);
        mod4N.CopyFrom(mod, numWords: n);

        var hostExp = exp.ToHostArray();

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

        resultBuffer.CopyFrom(tempResult, numWords: n);
    }

    /// <summary>
    /// Computes the inverse square root approximation of the operand using Newton-Raphson iterations on the GPU.
    /// X ≈ 2^B / sqrt(A)
    /// </summary>
    private static void InverseSqrt(GpuBigInteger targetOperand, GpuBigInteger resultBuffer)
    {
        var n = targetOperand.NumWords;
        if (resultBuffer.NumWords != n)
        {
            throw new ArgumentException("Result buffer for InverseSqrt must be n words.");
        }

        var actualBits = ComputeInitialGuess(targetOperand, resultBuffer, isSquareRoot: true);

        // 3. GPU Newton-Raphson iterations
        using var constant3 = new GpuBigInteger(targetOperand.Context, 3 * n);
        constant3.Clear();
        var hostConstant3 = new uint[3 * n];
        var bit2B = 2 * actualBits;
        hostConstant3[bit2B / 32] |= 1u << (bit2B % 32);
        hostConstant3[(bit2B + 1) / 32] |= 1u << ((bit2B + 1) % 32);
        targetOperand.Context.Provider.CopyHostToDevice(constant3.DevicePointer, hostConstant3, 3 * n);

        using var xSquared = new GpuBigInteger(targetOperand.Context, 2 * n);
        using var axSquared = new GpuBigInteger(targetOperand.Context, 3 * n);
        using var diff = new GpuBigInteger(targetOperand.Context, 3 * n);
        using var nextXUnshifted = new GpuBigInteger(targetOperand.Context, 4 * n);
        using var nextXShifted = new GpuBigInteger(targetOperand.Context, 4 * n);

        var targetBits = actualBits / 2;
        var currentBits = 31; // X_top has 31 bits
        var iterations = 0;
        while (currentBits < targetBits)
        {
            currentBits *= 2;
            iterations++;
        }

        iterations++; // One extra iteration for safety

        for (var i = 0; i < iterations; i++)
        {
            GpuBigIntMath.Mul(resultBuffer, resultBuffer, xSquared);
            GpuBigIntMath.Mul(targetOperand, xSquared, axSquared);
            GpuBigIntMath.Sub(constant3, axSquared, diff);
            GpuBigIntMath.Mul(resultBuffer, diff, nextXUnshifted);

            // Shift right by 2B + 1
            nextXShifted.Clear();
            GpuBigIntMath.ShiftRight(nextXUnshifted, 2 * actualBits + 1, nextXShifted);
            resultBuffer.CopyFrom(nextXShifted, destOffsetWords: 0, srcOffsetWords: 0, numWords: n);
        }
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
    public static void Sqrt(GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        var n = leftOperand.NumWords;
        var hostData = leftOperand.ToHostArray();
        var highestBit = -1;
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

            highestBit = i * 32 + b;
            break;
        }

        if (highestBit == -1)
        {
            resultBuffer.ClearWord(0);
            resultBuffer.CopyFrom(resultBuffer, numWords: 0);
            var zeros = new uint[n];
            resultBuffer.Context.Provider.CopyHostToDevice(resultBuffer.DevicePointer, zeros, n);
            return;
        }

        var actualBits = highestBit + 1;
        var lz = n * 32 - actualBits;
        if (lz % 2 != 0)
        {
            lz--;
        }

        using var aNorm = new GpuBigInteger(leftOperand.Context, n);
        aNorm.Clear();
        GpuBigIntMath.ShiftLeft(leftOperand, lz, aNorm);

        using var xResult = new GpuBigInteger(leftOperand.Context, n);
        xResult.Clear();
        InverseSqrt(aNorm, xResult);

        using var rNorm = new GpuBigInteger(leftOperand.Context, 2 * n);
        GpuBigIntMath.Mul(aNorm, xResult, rNorm);

        var bBits = actualBits + lz;
        var totalShift = bBits + (lz / 2);

        using var shiftedRNorm = new GpuBigInteger(leftOperand.Context, 2 * n);
        shiftedRNorm.Clear();
        GpuBigIntMath.ShiftRight(rNorm, totalShift, shiftedRNorm);

        using var currentRoot = new GpuBigInteger(leftOperand.Context, n);
        currentRoot.CopyFrom(shiftedRNorm, destOffsetWords: 0, srcOffsetWords: 0, numWords: n);

        // One final Newton-Raphson Division step to ensure exact integer precision
        using var divisionResult = new GpuBigInteger(leftOperand.Context, n);
        using var sumWithDivision = new GpuBigInteger(leftOperand.Context, n);
        Div(leftOperand, currentRoot, divisionResult);
        GpuBigIntMath.Add(currentRoot, divisionResult, sumWithDivision);
        currentRoot.Clear();
        GpuBigIntMath.ShiftRight(sumWithDivision, 1, currentRoot);

        using var squaredRoot = new GpuBigInteger(leftOperand.Context, 2 * n);
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
            using var lowerSquaredRoot = new GpuBigInteger(leftOperand.Context, n);
            lowerSquaredRoot.CopyFrom(squaredRoot, numWords: n);
            isGreater = GpuBigIntMath.IsLessThan(leftOperand, lowerSquaredRoot);
        }

        if (isGreater)
        {
            using var one = new GpuBigInteger(leftOperand.Context, n);
            one.Context.Provider.CopyHostToDevice(one.DevicePointer, [1u], 1);
            GpuBigIntMath.Sub(currentRoot, one, resultBuffer);
        }
        else
        {
            resultBuffer.CopyFrom(currentRoot);
        }
    }

    private static BigInteger ExtractTopBitsAsBigInteger(GpuBigInteger operand, out int actualBits)
    {
        actualBits = GetActualBits(operand);
        if (actualBits == 0)
        {
            throw new DivideByZeroException();
        }

        var hostData = operand.ToHostArray();
        var topIndex = (actualBits - 1) / 32;

        var wordsToExtract = System.Math.Min(4, topIndex + 1);
        var extractBytes = new byte[wordsToExtract * 4 + 1];
        Buffer.BlockCopy(hostData, (topIndex - wordsToExtract + 1) * 4, extractBytes, 0, wordsToExtract * 4);
        return new BigInteger(extractBytes);
    }

    private static int ComputeInitialGuess(GpuBigInteger targetOperand, GpuBigInteger resultBuffer, bool isSquareRoot)
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
        if (shiftInTruncated < 0)
        {
            shiftInTruncated = 0;
        }

        if (isSquareRoot && shiftInTruncated % 2 != 0)
        {
            shiftInTruncated++;
        }

        var originalBigTop = topBig >> shiftInTruncated;

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

        if (isSquareRoot)
        {
            var root = IntegerSqrt(originalBigTop);
            initialApproximation = (BigInteger.One << 62) / root;
            finalShift = actualBits - 62 - (shift / 2);
        }
        else
        {
            initialApproximation = (BigInteger.One << 124) / originalBigTop;
            finalShift = 2 * numBits - (124 + shift);
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

        var xData = new uint[resultBuffer.NumWords];
        var xBytes = shiftedApproximation.ToByteArray(isUnsigned: true, isBigEndian: false);
        Buffer.BlockCopy(xBytes, 0, xData, 0, System.Math.Min(xBytes.Length, xData.Length * 4));

        using var temp = new GpuBigInteger(targetOperand.Context, xData);
        resultBuffer.CopyFrom(temp);

        return actualBits;
    }
}



