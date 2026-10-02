using System.Buffers;

namespace Kassyi.GSharp.Stdlib.Math.GpuBigInt;

public static partial class GpuBigIntDivRem
{
    internal static void InverseSqrt(GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords = 0)
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

        var targetBits = actualBits + marginWords * 32;
        var currentBits = 31; // X_top has 31 bits

        ComputeInitialGuess(targetOperand, resultBuffer, targetBits, isSquareRoot: true);

        var iterations = CalculateNewtonRaphsonIterationCount(currentBits, targetBits);

        if (targetOperand.Workspace.Provider is { } provider)
        {
            provider.InverseSqrtMega(targetOperand.Workspace, targetOperand, resultBuffer, marginWords, actualBits, iterations);
        }
        else
        {
            throw new NotSupportedException("The current provider does not support InverseSqrtMega.");
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
            leftOperand.Workspace.Provider.CopyDeviceToHost(hostDataSingle, leftOperand.DevicePointer, n, leftOperand.Workspace.Stream);
            var val = hostDataSingle[0];
            var sqrtVal = (uint)System.Math.Sqrt(val);
            resultBuffer.Clear();
            if (sqrtVal > 0)
            {
                resultBuffer.Workspace.Provider.CopyHostToDevice(resultBuffer.DevicePointer, [sqrtVal], 1, resultBuffer.Workspace.Stream);
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

        using var aNorm = new GpuUIntBigInteger(leftOperand.Workspace, n);
        aNorm.Clear();
        GpuBigIntMath.ShiftLeft(leftOperand, lz, aNorm);

        var marginWords = 16;
        using var xResult = new GpuUIntBigInteger(leftOperand.Workspace, n + marginWords);
        xResult.Clear();
        InverseSqrt(aNorm, xResult, marginWords);

        using var rNorm = new GpuUIntBigInteger(leftOperand.Workspace, 2 * n + marginWords);
        GpuBigIntMath.Mul(aNorm, xResult, rNorm);

        var bBits = actualBits + lz;
        var marginBits = marginWords * 32;
        var totalShift = bBits + marginBits + (lz / 2);

        using var shiftedRNorm = new GpuUIntBigInteger(leftOperand.Workspace, 2 * n + marginWords);
        shiftedRNorm.Clear();
        GpuBigIntMath.ShiftRight(rNorm, totalShift, shiftedRNorm);

        var currentRoot = new GpuUIntBigInteger(leftOperand.Workspace, n);
        currentRoot.CopyFrom(shiftedRNorm, destOffsetWords: 0, srcOffsetWords: 0, numWords: n);
        
        if (GetActualBits(currentRoot) == 0)
        {
            currentRoot.Workspace.Provider.CopyHostToDevice(currentRoot.DevicePointer, [1u], 1, currentRoot.Workspace.Stream);
        }

        return currentRoot;
    }

    private static void RefineSqrtWithHeronMethod(GpuUIntBigInteger leftOperand, GpuUIntBigInteger currentRoot, GpuUIntBigInteger resultBuffer)
    {
        var n = leftOperand.NumWords;
        using var divisionResult = new GpuUIntBigInteger(leftOperand.Workspace, n);
        using var sumWithDivision = new GpuUIntBigInteger(leftOperand.Workspace, n);
        using var prevRoot = new GpuUIntBigInteger(leftOperand.Workspace, n);

        var maxIterations = 30;
        var iter = 0;
        while (iter++ < maxIterations)
        {
            prevRoot.CopyFrom(currentRoot, numWords: n);

            Div(leftOperand, currentRoot, divisionResult);
            GpuBigIntMath.Add(currentRoot, divisionResult, sumWithDivision);
            currentRoot.Clear();
            GpuBigIntMath.ShiftRight(sumWithDivision, 1, currentRoot);

            using var diff = new GpuUIntBigInteger(leftOperand.Workspace, n);
            GpuBigIntMath.BitwiseXor(currentRoot, prevRoot, diff);
            if (GetActualBits(diff) == 0)
            {
                break;
            }

            if (GetActualBits(currentRoot) == 0)
            {
                break;
            }

            using var plusOne = new GpuUIntBigInteger(leftOperand.Workspace, n);
            plusOne.Workspace.Provider.CopyHostToDevice(plusOne.DevicePointer, [1u], 1, plusOne.Workspace.Stream);
            using var prevPlusOne = new GpuUIntBigInteger(leftOperand.Workspace, n);
            GpuBigIntMath.Add(prevRoot, plusOne, prevPlusOne);

            GpuBigIntMath.BitwiseXor(currentRoot, prevPlusOne, diff);
            if (GetActualBits(diff) == 0)
            {
                currentRoot.CopyFrom(prevRoot, numWords: n);
                break;
            }
        }

        using var squaredRoot = new GpuUIntBigInteger(leftOperand.Workspace, 2 * n);
        GpuBigIntMath.Mul(currentRoot, currentRoot, squaredRoot);

        var topHalf = new uint[n];
        leftOperand.Workspace.Provider.CopyDeviceToHost(topHalf, IntPtr.Add(squaredRoot.DevicePointer, n * 4), n, leftOperand.Workspace.Stream);
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
            using var lowerSquaredRoot = new GpuUIntBigInteger(leftOperand.Workspace, n);
            lowerSquaredRoot.CopyFrom(squaredRoot, numWords: n);
            isGreater = GpuBigIntMath.IsLessThan(leftOperand, lowerSquaredRoot);
        }

        if (isGreater)
        {
            using var one = new GpuUIntBigInteger(leftOperand.Workspace, n);
            one.Workspace.Provider.CopyHostToDevice(one.DevicePointer, [1u], 1, one.Workspace.Stream);
            GpuBigIntMath.Sub(currentRoot, one, resultBuffer);
        }
        else
        {
            resultBuffer.CopyFrom(currentRoot);
        }
    }


}

