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
        var hostData = targetOperand.ToHostArray();

        // 1. Find the highest non-zero bit directly from the array
        var actualBits = 0;
        var topIndex = -1;
        for (int i = hostData.Length - 1; i >= 0; i--)
        {
            if (hostData[i] != 0)
            {
                var w = hostData[i];
                var b = 31;
                while ((w & (1u << b)) == 0)
                {
                    b--;
                }

                actualBits = i * 32 + b + 1;
                topIndex = i;
                break;
            }
        }

        if (actualBits == 0)
        {
            throw new DivideByZeroException();
        }

        // 2. Extract only the top words to form a small BigInteger (at most 4 words = 128 bits)
        int wordsToExtract = System.Math.Min(4, topIndex + 1);
        var extractBytes = new byte[wordsToExtract * 4 + 1];
        Buffer.BlockCopy(hostData, (topIndex - wordsToExtract + 1) * 4, extractBytes, 0, wordsToExtract * 4);
        var topBigB = new BigInteger(extractBytes);

        var truncatedBits = 0;
        var tb = topBigB;
        while (tb > 0)
        {
            truncatedBits++;
            tb >>= 1;
        }

        var shiftInTruncated = truncatedBits - 62;
        if (shiftInTruncated < 0)
        {
            shiftInTruncated = 0;
        }

        var originalBigTop = topBigB >> shiftInTruncated;

        var shift = actualBits - 62;
        if (shift < 0)
        {
            shift = 0;
        }

        var initialApproximation = (BigInteger.One << 124) / originalBigTop;
        var finalShift = 2 * numBits - (124 + shift);
        var shiftedApproximation = initialApproximation;
        if (finalShift >= 0)
        {
            shiftedApproximation <<= finalShift;
        }
        else
        {
            shiftedApproximation >>= -finalShift;
        }

        var xData = new uint[2 * n];
        var xBytes = shiftedApproximation.ToByteArray(isUnsigned: true, isBigEndian: false);
        Buffer.BlockCopy(xBytes, 0, xData, 0, System.Math.Min(xBytes.Length, xData.Length * 4));

        resultBuffer.CopyFrom(new GpuBigInteger(targetOperand.Context, xData));

        using var constant = GpuBigInteger.CreatePowerOfTwo(targetOperand.Context, 3 * n, 2 * numBits + 1);
        using var currentProduct = new GpuBigInteger(targetOperand.Context, 3 * n);
        using var currentDifference = new GpuBigInteger(targetOperand.Context, 3 * n);
        using var nextIterationValue = new GpuBigInteger(targetOperand.Context, 5 * n);

        int targetBits = 2 * numBits;
        int currentBits = 124;
        int iterations = 0;
        while (currentBits < targetBits)
        {
            currentBits *= 2;
            iterations++;
        }

        iterations++; // One extra iteration for safety to ensure full convergence

        for (var i = 0; i < iterations; i++)
        {
            GpuBigIntMath.Mul(targetOperand, resultBuffer, currentProduct);
            GpuBigIntMath.Sub(constant, currentProduct, currentDifference);
            GpuBigIntMath.Mul(resultBuffer, currentDifference, nextIterationValue);

            resultBuffer.CopyFrom(nextIterationValue, destOffsetWords: 0, srcOffsetWords: 2 * n, numWords: 2 * n);
        }
    }

    public static void DivRem(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger quotientBuffer,
        GpuBigInteger remainderBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, quotientBuffer);
        ThrowIfSizeMismatch(leftOperand, remainderBuffer);

        var n = rightOperand.NumWords;
        int bActualBits = GetActualBits(rightOperand);
        int lz = n * 32 - bActualBits;

        using var bNorm = new GpuBigInteger(rightOperand.Context, n);
        bNorm.Clear();
        GpuBigIntMath.ShiftLeft(rightOperand, lz, bNorm);

        using var reciprocalNorm = new GpuBigInteger(rightOperand.Context, 2 * n);
        reciprocalNorm.Clear();
        Reciprocal(bNorm, reciprocalNorm);

        using var reciprocalOfB = new GpuBigInteger(rightOperand.Context, 2 * n);
        GpuBigIntMath.ShiftLeft(reciprocalNorm, lz, reciprocalOfB);

        using var productWithReciprocal = new GpuBigInteger(rightOperand.Context, 3 * n);
        GpuBigIntMath.Mul(leftOperand, reciprocalOfB, productWithReciprocal);

        quotientBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: 2 * n, numWords: n);

        using var qb = new GpuBigInteger(rightOperand.Context, 2 * n);
        GpuBigIntMath.Mul(quotientBuffer, rightOperand, qb);

        using var leftOperandExtended = new GpuBigInteger(rightOperand.Context, 2 * n);
        leftOperandExtended.Clear();
        leftOperandExtended.CopyFrom(leftOperand, numWords: n);

        var zeroArray = new uint[n];
        rightOperand.Context.Provider.CopyHostToDevice(IntPtr.Add(leftOperandExtended.DevicePointer, n * 4), zeroArray,
            n);

        using var remainderExtended = new GpuBigInteger(rightOperand.Context, 2 * n);
        GpuBigIntMath.Sub(leftOperandExtended, qb, remainderExtended);

        var remainderBits = GetActualBits(remainderExtended);
        var underflow = remainderBits >= n * 32;

        if (underflow)
        {
            using var bPaddedExtended = new GpuBigInteger(rightOperand.Context, 2 * n);
            bPaddedExtended.Clear();
            bPaddedExtended.CopyFrom(rightOperand, numWords: n);

            CorrectUnderflow(rightOperand.Context, n, quotientBuffer, remainderExtended, bPaddedExtended);
        }
        else
        {
            using var bPaddedExtended = new GpuBigInteger(rightOperand.Context, 2 * n);
            bPaddedExtended.Clear();
            bPaddedExtended.CopyFrom(rightOperand, numWords: n);

            CorrectRemainder(rightOperand.Context, n, quotientBuffer, remainderExtended, bPaddedExtended);
        }

        remainderBuffer.CopyFrom(remainderExtended, numWords: n);
    }

    private static void LogTopWords(string name, GpuBigInteger gpuInt)
    {
        var bits = GetActualBits(gpuInt);
        var arr = gpuInt.ToHostArray();
        var wordIdx = bits == 0 ? 0 : (bits - 1) / 32;
        var top0 = arr[wordIdx];
        var top1 = wordIdx >= 1 ? arr[wordIdx - 1] : 0;
        var top2 = wordIdx >= 2 ? arr[wordIdx - 2] : 0;
        var top3 = wordIdx >= 3 ? arr[wordIdx - 3] : 0;
        Logger<GpuContext>.Debug($"[{name}] Top 4 Words: {top0:X8} {top1:X8} {top2:X8} {top3:X8}");
    }

    private static void CorrectUnderflow(GpuContext context, int n, GpuBigInteger quotientBuffer,
        GpuBigInteger remainderExtended, GpuBigInteger bPaddedExtended)
    {
        int loopCount = 0;
        using var one = GpuBigInteger.CreatePowerOfTwo(context, n, 0);
        using var tempQuot = new GpuBigInteger(context, n);
        using var tempRem = new GpuBigInteger(context, 2 * n);

        bool underflow = true;
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
            remainderExtended.CopyFrom(tempRem, numWords: 2 * n);

            var topWord = remainderExtended.GetTopWords(1)[0];
            underflow = (topWord & 0x80000000u) != 0;
        }
    }

    private static void CorrectRemainder(GpuContext context, int n, GpuBigInteger quotientBuffer,
        GpuBigInteger remainderExtended, GpuBigInteger bPaddedExtended)
    {
        int loopCount = 0;
        using var tempQuot = new GpuBigInteger(context, n);
        using var tempRem = new GpuBigInteger(context, 2 * n);
        using var shiftedB = new GpuBigInteger(context, 2 * n);
        using var oneFallback = GpuBigInteger.CreatePowerOfTwo(context, n, 0);

        while (!GpuBigIntMath.IsLessThan(remainderExtended, bPaddedExtended))
        {
            loopCount++;
            if (loopCount > 10000)
            {
                throw new Exception("DivRem remainder correction failed to converge.");
            }

            int rBits = GetActualBits(remainderExtended);
            int bBits = GetActualBits(bPaddedExtended);

            if (rBits > bBits + 1)
            {
                int shift = rBits - bBits - 1;
                if (shift > 0)
                {
                    GpuBigIntMath.ShiftLeft(bPaddedExtended, shift, shiftedB);

                    if (GpuBigIntMath.IsLessThan(remainderExtended, shiftedB))
                    {
                        shift--;
                        if (shift > 0)
                        {
                            GpuBigIntMath.ShiftLeft(bPaddedExtended, shift, shiftedB);
                            GpuBigIntMath.Sub(remainderExtended, shiftedB, tempRem);
                            remainderExtended.CopyFrom(tempRem, numWords: 2 * n);

                            using var correction = GpuBigInteger.CreatePowerOfTwo(context, n, shift);
                            GpuBigIntMath.Add(quotientBuffer, correction, tempQuot);
                            quotientBuffer.CopyFrom(tempQuot, numWords: n);
                        }
                        else
                        {
                            GpuBigIntMath.Sub(remainderExtended, bPaddedExtended, tempRem);
                            remainderExtended.CopyFrom(tempRem, numWords: 2 * n);

                            GpuBigIntMath.Add(quotientBuffer, oneFallback, tempQuot);
                            quotientBuffer.CopyFrom(tempQuot, numWords: n);
                        }
                    }
                    else
                    {
                        GpuBigIntMath.Sub(remainderExtended, shiftedB, tempRem);
                        remainderExtended.CopyFrom(tempRem, numWords: 2 * n);

                        using var correction = GpuBigInteger.CreatePowerOfTwo(context, n, shift);
                        GpuBigIntMath.Add(quotientBuffer, correction, tempQuot);
                        quotientBuffer.CopyFrom(tempQuot, numWords: n);
                    }

                    continue;
                }
            }

            GpuBigIntMath.Sub(remainderExtended, bPaddedExtended, tempRem);
            remainderExtended.CopyFrom(tempRem, numWords: 2 * n);

            GpuBigIntMath.Add(quotientBuffer, oneFallback, tempQuot);
            quotientBuffer.CopyFrom(tempQuot, numWords: n);
        }
    }

    private static int GetActualBits(GpuBigInteger gpuBigInt)
    {
        var hostData = gpuBigInt.ToHostArray();
        for (int i = hostData.Length - 1; i >= 0; i--)
        {
            if (hostData[i] != 0)
            {
                var w = hostData[i];
                var b = 31;
                while ((w & (1u << b)) == 0)
                {
                    b--;
                }

                return i * 32 + b + 1;
            }
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
                    if (hostExp[j] != 0)
                    {
                        allZero = false;
                        break;
                    }
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

        var numBits = n * 32;
        var hostData = targetOperand.ToHostArray();

        // 1. Find the highest non-zero bit directly from the array
        var actualBits = GetActualBits(targetOperand);
        if (actualBits == 0)
        {
            throw new DivideByZeroException();
        }

        var topIndex = (actualBits - 1) / 32;

        // 2. Extract only the top words to form a small BigInteger (at most 4 words = 128 bits)
        int wordsToExtract = System.Math.Min(4, topIndex + 1);
        var extractBytes = new byte[wordsToExtract * 4 + 1];
        Buffer.BlockCopy(hostData, (topIndex - wordsToExtract + 1) * 4, extractBytes, 0, wordsToExtract * 4);
        var topBigA = new BigInteger(extractBytes);

        var truncatedBits = 0;
        var ta = topBigA;
        while (ta > 0)
        {
            truncatedBits++;
            ta >>= 1;
        }

        var shiftInTruncated = truncatedBits - 62;
        if (shiftInTruncated < 0)
        {
            shiftInTruncated = 0;
        }

        // Ensure the shift is even so shift/2 is exact
        if (shiftInTruncated % 2 != 0)
        {
            shiftInTruncated++;
        }

        var originalBigTop = topBigA >> shiftInTruncated;
        
        var shift = actualBits - 62;
        if (shift < 0)
        {
            shift = 0;
        }

        if (shift % 2 != 0)
        {
            shift++; // Ensure even shift from true actual bits
        }

        // Calculate CPU initial guess for X_top = 2^62 / sqrt(A_top)
        // Since A_top has 62 bits, sqrt(A_top) has 31 bits, X_top has 31 bits.
        var root = IntegerSqrt(originalBigTop);
        var initialApproximation = (BigInteger.One << 62) / root;

        var shiftX = actualBits - 62 - (shift / 2);
        
        var shiftedApproximation = initialApproximation;
        if (shiftX >= 0)
        {
            shiftedApproximation <<= shiftX;
        }
        else
        {
            shiftedApproximation >>= -shiftX;
        }

        var xData = new uint[n];
        var xBytes = shiftedApproximation.ToByteArray(isUnsigned: true, isBigEndian: false);
        Buffer.BlockCopy(xBytes, 0, xData, 0, System.Math.Min(xBytes.Length, xData.Length * 4));

        resultBuffer.CopyFrom(new GpuBigInteger(targetOperand.Context, xData));

        // 3. GPU Newton-Raphson iterations
        using var constant3 = new GpuBigInteger(targetOperand.Context, 3 * n);
        constant3.Clear();
        var hostConstant3 = new uint[3 * n];
        int bit2B = 2 * actualBits;
        hostConstant3[bit2B / 32] |= 1u << (bit2B % 32);
        hostConstant3[(bit2B + 1) / 32] |= 1u << ((bit2B + 1) % 32);
        targetOperand.Context.Provider.CopyHostToDevice(constant3.DevicePointer, hostConstant3, 3 * n);

        using var xSquared = new GpuBigInteger(targetOperand.Context, 2 * n);
        using var axSquared = new GpuBigInteger(targetOperand.Context, 3 * n);
        using var diff = new GpuBigInteger(targetOperand.Context, 3 * n);
        using var nextXUnshifted = new GpuBigInteger(targetOperand.Context, 4 * n);
        using var nextXShifted = new GpuBigInteger(targetOperand.Context, 4 * n);

        int targetBits = actualBits / 2;
        int currentBits = 31; // X_top has 31 bits
        int iterations = 0;
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

        BigInteger guess = value >> (int)(value.GetBitLength() / 2);
        if (guess == 0)
        {
            guess = 1;
        }

        while (true)
        {
            BigInteger nextGuess = (guess + value / guess) >> 1;
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

        int actualBits = highestBit + 1;
        int lz = n * 32 - actualBits;
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

        int bBits = actualBits + lz;
        int totalShift = bBits + (lz / 2);

        using var shiftedRNorm = new GpuBigInteger(leftOperand.Context, 2 * n);
        GpuBigIntMath.ShiftRight(rNorm, totalShift, shiftedRNorm);

        using var currentRoot = new GpuBigInteger(leftOperand.Context, n);
        currentRoot.CopyFrom(shiftedRNorm, destOffsetWords: 0, srcOffsetWords: 0, numWords: n);

        // One final Newton-Raphson Division step to ensure exact integer precision
        using var divisionResult = new GpuBigInteger(leftOperand.Context, n);
        using var sumWithDivision = new GpuBigInteger(leftOperand.Context, n);
        Div(leftOperand, currentRoot, divisionResult);
        GpuBigIntMath.Add(currentRoot, divisionResult, sumWithDivision);
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
}



