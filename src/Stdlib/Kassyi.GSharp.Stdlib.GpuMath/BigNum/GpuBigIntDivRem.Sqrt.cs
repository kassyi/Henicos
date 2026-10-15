namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

public static partial class GpuBigIntDivRem
{
    internal static void InverseSqrt(GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int marginWords = 0)
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


    /// <summary>
    /// Computes the integer square root ($\lfloor\sqrt{\text{leftOperand}}\rfloor$) on the GPU using inverse square root approximation and Heron's refinement.
    /// </summary>
    /// <param name="leftOperand">The non-negative big integer operand.</param>
    /// <param name="resultBuffer">The destination buffer receiving the integer square root.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leftOperand"/> and <paramref name="resultBuffer"/> have mismatched word lengths.</exception>
    /// <remarks>
    /// <para><b>Algorithm:</b> First calculates an initial guess via Newton-Raphson inverse square root on the GPU, then refines it using Heron's method to guarantee the exact mathematical floor integer square root.</para>
    /// </remarks>
    public static void Sqrt(GpuBigUInt leftOperand, GpuBigUInt resultBuffer)
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
    private static bool TryComputeSingleWordSqrt(GpuBigUInt leftOperand, GpuBigUInt resultBuffer, int actualBits)
    {
        if (actualBits > 32)
        {
            return false;
        }

        // Only word 0 can be non-zero here, so a single-word read suffices instead of transferring every limb.
        var val = leftOperand.Workspace.Provider.ReadWord(leftOperand.DevicePointer, 0, leftOperand.Workspace.Stream);
        var sqrtVal = (uint)Math.Sqrt(val);
        resultBuffer.Clear();
        if (sqrtVal > 0)
        {
            resultBuffer.Workspace.Provider.WriteWord(resultBuffer.DevicePointer, 0, sqrtVal,
                resultBuffer.Workspace.Stream);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static GpuBigUInt ComputeInitialSqrtGuess(GpuBigUInt leftOperand, int actualBits)
    {
        var n = leftOperand.NumWords;
        var lz = n * 32 - actualBits;
        if (lz % 2 != 0)
        {
            lz--;
        }

        using var aNorm = new GpuBigUInt(leftOperand.Workspace, n);
        aNorm.Clear();
        GpuBigIntMath.ShiftLeft(leftOperand, lz, aNorm);

        var marginWords = 16;
        using var xResult = new GpuBigUInt(leftOperand.Workspace, n + marginWords);
        xResult.Clear();
        InverseSqrt(aNorm, xResult, marginWords);

        using var rNorm = new GpuBigUInt(leftOperand.Workspace, 2 * n + marginWords);
        GpuBigIntMath.Mul(aNorm, xResult, rNorm);

        var bBits = actualBits + lz;
        var marginBits = marginWords * 32;
        var totalShift = bBits + marginBits + (lz / 2);

        using var shiftedRNorm = new GpuBigUInt(leftOperand.Workspace, 2 * n + marginWords);
        shiftedRNorm.Clear();
        GpuBigIntMath.ShiftRight(rNorm, totalShift, shiftedRNorm);

        var currentRoot = new GpuBigUInt(leftOperand.Workspace, n);
        currentRoot.CopyFrom(shiftedRNorm, destOffsetWords: 0, srcOffsetWords: 0, numWords: n);

        if (GetActualBits(currentRoot) == 0)
        {
            currentRoot.Workspace.Provider.CopyHostToDevice(currentRoot.DevicePointer, [1u], 1, currentRoot.Workspace.Stream);
        }

        return currentRoot;
    }

    private static void RefineSqrtWithHeronMethod(GpuBigUInt leftOperand, GpuBigUInt currentRoot, GpuBigUInt resultBuffer)
    {
        var n = leftOperand.NumWords;
        using var divisionResult = new GpuBigUInt(leftOperand.Workspace, n);
        using var sumWithDivision = new GpuBigUInt(leftOperand.Workspace, n);
        using var prevRoot = new GpuBigUInt(leftOperand.Workspace, n);

        var maxIterations = 30;
        var iter = 0;
        while (iter++ < maxIterations)
        {
            prevRoot.CopyFrom(currentRoot, numWords: n);

            Div(leftOperand, currentRoot, divisionResult);
            GpuBigIntMath.Add(currentRoot, divisionResult, sumWithDivision);
            currentRoot.Clear();
            GpuBigIntMath.ShiftRight(sumWithDivision, 1, currentRoot);

            using var diff = new GpuBigUInt(leftOperand.Workspace, n);
            GpuBigIntMath.BitwiseXor(currentRoot, prevRoot, diff);
            if (GetActualBits(diff) == 0)
            {
                break;
            }

            if (GetActualBits(currentRoot) == 0)
            {
                break;
            }

            using var plusOne = new GpuBigUInt(leftOperand.Workspace, n);
            plusOne.Workspace.Provider.CopyHostToDevice(plusOne.DevicePointer, [1u], 1, plusOne.Workspace.Stream);
            using var prevPlusOne = new GpuBigUInt(leftOperand.Workspace, n);
            GpuBigIntMath.Add(prevRoot, plusOne, prevPlusOne);

            GpuBigIntMath.BitwiseXor(currentRoot, prevPlusOne, diff);
            if (GetActualBits(diff) == 0)
            {
                currentRoot.CopyFrom(prevRoot, numWords: n);
                break;
            }
        }

        using var squaredRoot = new GpuBigUInt(leftOperand.Workspace, 2 * n);
        GpuBigIntMath.Mul(currentRoot, currentRoot, squaredRoot);

        var isGreater = false;
        var topHalf = ArrayPool<uint>.Shared.Rent(n);
        try
        {
            leftOperand.Workspace.Provider.CopyDeviceToHost(topHalf, IntPtr.Add(squaredRoot.DevicePointer, n * 4), n, leftOperand.Workspace.Stream);
            for (var i = 0; i < n; i++)
            {
                if (topHalf[i] == 0)
                {
                    continue;
                }

                isGreater = true;
                break;
            }
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(topHalf);
        }

        if (!isGreater)
        {
            using var lowerSquaredRoot = new GpuBigUInt(leftOperand.Workspace, n);
            lowerSquaredRoot.CopyFrom(squaredRoot, numWords: n);
            isGreater = GpuBigIntMath.IsLessThan(leftOperand, lowerSquaredRoot);
        }

        if (isGreater)
        {
            using var one = new GpuBigUInt(leftOperand.Workspace, n);
            one.Workspace.Provider.WriteWord(one.DevicePointer, 0, 1u, one.Workspace.Stream);
            GpuBigIntMath.Sub(currentRoot, one, resultBuffer);
        }
        else
        {
            resultBuffer.CopyFrom(currentRoot);
        }
    }


}

