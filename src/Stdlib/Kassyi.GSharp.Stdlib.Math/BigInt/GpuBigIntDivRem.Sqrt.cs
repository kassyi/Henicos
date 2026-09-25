using System.Buffers;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

public static partial class GpuBigIntDivRem
{
    private static void InverseSqrt(GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords = 0)
    {
        var n = targetOperand.NumWords;
        if (resultBuffer.NumWords != n + marginWords)
        {
            throw new ArgumentException("Result buffer for InverseSqrt must be targetOperand.NumWords + marginWords.");
        }

        var actualBits = GetActualBits(targetOperand);
        if (actualBits == 0)
        {
            throw new DivideByZeroException();
        }

        var constant3Words = n + marginWords;
        var marginBits = marginWords * 32;
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

        var iterations = CalculateNewtonRaphsonIterationCount(currentBits, targetBits);

        for (var i = 0; i < iterations; i++)
        {
            GpuBigIntMath.Mul(resultBuffer, resultBuffer, xSquared);
            GpuBigIntMath.Mul(targetOperand, xSquared, axSquared);
            GpuBigIntMath.Sub(constant3, axSquared, diff);
            GpuBigIntMath.Mul(resultBuffer, diff, nextXUnshifted);

            // Shift right by 2S + 1
            nextXShifted.Clear();
            GpuBigIntMath.ShiftRight(nextXUnshifted, 2 * actualBits + 2 * marginBits + 1, nextXShifted);
            resultBuffer.CopyFrom(nextXShifted, destOffsetWords: 0, srcOffsetWords: 0, numWords: n + marginWords);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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


}

