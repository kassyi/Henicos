namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

public static partial class GpuBigIntDivRem
{
    /// <summary>
    /// Computes the inverse square root <c>floor(2^B / sqrt(a))</c>, <c>B = (n + marginWords) * 32</c>, of the operand
    /// <c>a</c> using Newton-Raphson iterations on the GPU, followed by an exact correction on the device.
    /// </summary>
    /// <param name="targetOperand">The operand <c>a</c> of <c>n</c> words, normalized with an even shift (its bit length
    /// is <c>32n</c> or <c>32n - 1</c>), as every caller passes it.</param>
    /// <param name="resultBuffer">Receives exactly <c>floor(2^B / sqrt(a))</c>, the largest <c>x</c> with
    /// <c>a * x^2 &lt;= 2^(2B)</c>; it must hold <c>n + marginWords</c> words.</param>
    /// <param name="marginWords">The extra words of precision above the operand size.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resultBuffer"/> does not hold <c>n + marginWords</c> words.</exception>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="targetOperand"/> is zero.</exception>
    /// <remarks>
    /// The kernel computes <c>s = 2^(2B) - a * x^2</c> after the iterations and moves <c>x</c> one step at a time until
    /// <c>0 &lt;= s &lt; a * (2x + 1)</c>, without a host synchronization. If 1000 steps do not
    /// suffice, the next read of the workspace stream throws <see cref="InvalidOperationException"/>.
    /// </remarks>
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

        var targetBits = (n + marginWords) * 32;
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

    /// <summary>
    /// Computes <c>floor(sqrt(a))</c> exactly for an operand <c>a</c> of <c>2n</c> words with
    /// <c>2^(64n - 2) &lt;= a &lt; 2^(64n)</c>, so that the root has exactly <c>32n</c> bits.
    /// </summary>
    /// <param name="operand">The operand <c>a</c>, of <c>2 * root.NumWords</c> words and bit length <c>64n</c> or
    /// <c>64n - 1</c>.</param>
    /// <param name="root">Receives <c>floor(sqrt(a))</c> (<c>n</c> words).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="operand"/> does not have <c>2n</c> words. Only the
    /// word count is checked: the bit length <c>64n</c> or <c>64n - 1</c> is the caller's precondition, and another bit
    /// length gives a wrong root or a correction failure.</exception>
    /// <remarks>
    /// <para>
    /// The top <c>n + 1</c> words <c>A_top = floor(a / 2^j)</c>, <c>j = 32(n - 1)</c>, have the bit length that
    /// <see cref="InverseSqrt"/> needs. With <c>m = ceil(n / 2)</c> margin words it gives
    /// <c>x = floor(2^B / sqrt(A_top))</c>, <c>B = 32(n + 1 + m)</c>, and
    /// <c>r' = floor(A_top·x / 2^(16n + 32m + 48))</c> lies within <c>2^-16 + 2^-32</c> below <c>sqrt(a)</c>, so it is
    /// <c>floor(sqrt(a))</c> or one less (the floor of <c>x</c> loses less than <c>2^(16(n - 1) - 32m)</c>, the low words
    /// of <c>a</c> less than <c>2^-32</c>).
    /// </para>
    /// <para>
    /// <see cref="IGpuAdvancedMathProvider.SqrtFloorCorrection"/> then moves <c>r'</c> to the floor on the device, from
    /// <c>a - r'^2</c>: no host synchronization beyond those of <see cref="InverseSqrt"/>.
    /// </para>
    /// </remarks>
    internal static void SqrtFloor(GpuBigUInt operand, GpuBigUInt root)
    {
        var n = root.NumWords;
        if (operand.NumWords != 2 * n)
        {
            throw new ArgumentException(
                $"SqrtFloor needs an operand of twice the root words. operand: {operand.NumWords}, root: {n} words.");
        }

        var workspace = operand.Workspace;
        var marginWords = SqrtFloorMarginWords(n);

        // A_top: the top n + 1 words of a, borrowed without a copy.
        var top = new GpuBigIntMemoryHandle(operand.Handle.Buffer, operand.Handle.WordOffset + n - 1, n + 1)
            .AsBorrowedBigUInt(workspace);
        using var inverse = workspace.RentTemp(n + 1 + marginWords);
        InverseSqrt(top, inverse, marginWords);

        using var product = workspace.RentTemp(2 * (n + 1) + marginWords);
        GpuBigIntMath.Mul(top, inverse, product);
        GpuBigIntMath.ShiftAndResize(product, -SqrtFloorEstimateShift(n, marginWords), root);

        using var rootSquared = workspace.RentTemp(2 * n);
        GpuBigIntMath.Mul(root, root, rootSquared);
        using var slack = workspace.RentTemp(2 * n + 1);
        using var trial = workspace.RentTemp(2 * n + 1);
        workspace.Provider.SqrtFloorCorrection(workspace, operand, rootSquared, root, slack, trial);
    }

    /// <summary>
    /// The margin words <c>m = ceil(n / 2)</c> of the inverse square root behind the <see cref="SqrtFloor"/> estimate
    /// for a root of <paramref name="rootWords"/> words: enough to keep the error of the estimate below
    /// <c>2^-16 + 2^-32</c>.
    /// </summary>
    internal static int SqrtFloorMarginWords(int rootWords) => (rootWords + 1) / 2;

    /// <summary>
    /// The right shift that turns <c>A_top·x</c> into the <see cref="SqrtFloor"/> estimate:
    /// <c>B - j/2 = 32(n + 1 + m) - 16(n - 1) = 16n + 32m + 48</c>.
    /// </summary>
    internal static int SqrtFloorEstimateShift(int rootWords, int marginWords) =>
        16 * rootWords + 32 * marginWords + 48;

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

        var currentRoot = new GpuBigUInt(leftOperand.Workspace, n);
        try
        {
            GpuBigIntMath.ShiftAndResize(rNorm, -totalShift, currentRoot);

            if (GetActualBits(currentRoot) == 0)
            {
                currentRoot.Workspace.Provider.CopyHostToDevice(currentRoot.DevicePointer, [1u], 1, currentRoot.Workspace.Stream);
            }

            return currentRoot;
        }
        catch
        {
            currentRoot.Dispose();
            throw;
        }
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

