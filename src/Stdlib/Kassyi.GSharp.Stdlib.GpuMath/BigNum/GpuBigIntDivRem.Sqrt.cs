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
    internal static void InverseSqrt(
        scoped GpuBigUIntView targetOperand,
        scoped GpuBigUIntView resultBuffer,
        int marginWords = 0
    )
    {
        var operand = targetOperand.AsBorrowedBigUInt();
        var result = resultBuffer.AsBorrowedBigUInt();
        PrepareInverseSqrt(operand, result, marginWords, out var actualBits, out var iterations);
        operand.Workspace.Provider.InverseSqrtMega(
            operand.Workspace, operand, result, marginWords, actualBits, iterations);
    }

    /// <summary>
    /// The largest distance <c>floor(2^B / sqrt(a)) - x</c> of an <see cref="ApproximateInverseSqrt"/> result
    /// <c>x</c> below the floor.
    /// </summary>
    internal const int ApproximateInverseSqrtMaxError = 16;

    /// <summary>
    /// Computes the inverse square root of <see cref="InverseSqrt"/> without its final correction: the result is never
    /// above <c>floor(2^B / sqrt(a))</c> and at most <see cref="ApproximateInverseSqrtMaxError"/> below it.
    /// </summary>
    /// <param name="targetOperand">The operand <c>a</c>, normalized as for <see cref="InverseSqrt"/>.</param>
    /// <param name="resultBuffer">Receives the estimate; it must hold <c>n + marginWords</c> words.</param>
    /// <param name="marginWords">The extra words of precision above the operand size.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resultBuffer"/> does not hold <c>n + marginWords</c> words.</exception>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="targetOperand"/> is zero.</exception>
    /// <remarks>
    /// Only for a caller that corrects its own result exactly, such as <see cref="SqrtFloor"/>: it saves the three
    /// products and the step loop of the correction. The result stays below the root because every iteration computes
    /// its products exactly and only rounds down, and one exact Newton step <c>x(3 - a x^2) / 2</c> never passes
    /// <c>2^B / sqrt(a)</c>.
    /// </remarks>
    internal static void ApproximateInverseSqrt(
        scoped GpuBigUIntView targetOperand,
        scoped GpuBigUIntView resultBuffer,
        int marginWords
    )
    {
        var operand = targetOperand.AsBorrowedBigUInt();
        var result = resultBuffer.AsBorrowedBigUInt();
        PrepareInverseSqrt(operand, result, marginWords, out var actualBits, out var iterations);
        operand.Workspace.Provider.ApproximateInverseSqrtMega(
            operand.Workspace, operand, result, marginWords, actualBits, iterations);
    }

    // Validates the inverse-square-root buffers, writes the initial guess and computes the Newton iteration count.
    private static void PrepareInverseSqrt(GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int marginWords,
        out int actualBits, out int iterations)
    {
        var n = targetOperand.NumWords;
        if (resultBuffer.NumWords != n + marginWords)
        {
            throw new ArgumentException("Result buffer for InverseSqrt must be targetOperand.NumWords + marginWords.");
        }

        actualBits = GetActualBits(targetOperand);
        if (actualBits == 0)
        {
            throw new DivideByZeroException();
        }

        var targetBits = (n + marginWords) * 32;
        const int CurrentBits = 31; // X_top has 31 bits
        ComputeInitialGuess(targetOperand, resultBuffer, targetBits, isSquareRoot: true);
        iterations = CalculateNewtonRaphsonIterationCount(CurrentBits, targetBits);
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
    /// <see cref="ApproximateInverseSqrt"/> needs. With <c>m = ceil(n / 2)</c> margin words it gives
    /// <c>x</c> at most <c>E = </c><see cref="ApproximateInverseSqrtMaxError"/> below <c>2^B / sqrt(A_top)</c>,
    /// <c>B = 32(n + 1 + m)</c>, and <c>r' = floor(A_top·x / 2^(16n + 32m + 48))</c> lies within
    /// <c>(E + 1)·2^-16 + 2^-32</c> below <c>sqrt(a)</c>, so it is <c>floor(sqrt(a))</c> or one less (each unit of
    /// <c>x</c> moves the estimate by less than <c>2^(16(n - 1) - 32m)</c>, the low words of <c>a</c> by less than
    /// <c>2^-32</c>).
    /// </para>
    /// <para>
    /// <see cref="IGpuAdvancedMathProvider.SqrtFloorCorrection"/> then moves <c>r'</c> to the floor on the device, from
    /// <c>a - r'^2</c>: no host synchronization beyond those of <see cref="InverseSqrt"/>.
    /// </para>
    /// <para>
    /// A root wider than <see cref="GpuWorkspace.SqrtPrecisionDoublingThresholdWords"/> words takes <c>y</c>, the
    /// inverse square root of <c>A_top</c> to half the precision, by precision doubling driven from the host,
    /// <c>A_t = floor(A_top / 2^(32j))</c> (<c>j = floor(n / 2)</c>), the half-precision root
    /// <c>s0 = floor(A_t·y / 2^(P - 32j))</c>, <c>s = s0·2^(16n - 16)</c>, and one Karp-Markstein step
    /// <c>s + floor(y·floor((A_top - s0^2) / 2^31) / 2^(32 p_y))</c>, which is <c>floor(sqrt(a))</c> or one less; the
    /// same correction follows.
    /// </para>
    /// </remarks>
    internal static void SqrtFloor(GpuBigUInt operand, GpuBigUInt root)
    {
        ThrowIfNotTwiceTheRoot(operand, root);
        var workspace = operand.Workspace;
        if (root.NumWords > workspace.SqrtPrecisionDoublingThresholdWords)
        {
            // Wide roots: half-precision inverse square root by precision doubling and one Karp-Markstein step
            workspace.CountInverseSqrtDoubling();
            SqrtFloorKarpMarkstein(operand, root, trace: null);
            return;
        }

        SqrtFloorMegaKernel(operand, root);
    }

    private static void SqrtFloorMegaKernel(GpuBigUInt operand, GpuBigUInt root)
    {
        var n = root.NumWords;
        var workspace = operand.Workspace;
        var marginWords = SqrtFloorMarginWords(n);

        // A_top: the top n + 1 words of a, borrowed without a copy.
        var top = operand.Handle.Slice(n - 1, n + 1).AsView(workspace);
        using var inverse = workspace.RentTemp(n + 1 + marginWords);
        ApproximateInverseSqrt(top, inverse, marginWords);

        using var product = workspace.RentTemp(2 * (n + 1) + marginWords);
        GpuBigIntMath.Mul(top, inverse, product);
        GpuBigIntMath.ShiftAndResize(product, -SqrtFloorEstimateShift(n, marginWords), root);

        CorrectSqrtFloor(operand, root);
    }

    private static void ThrowIfNotTwiceTheRoot(GpuBigUInt operand, GpuBigUInt root)
    {
        var n = root.NumWords;
        if (operand.NumWords != 2 * n)
        {
            throw new ArgumentException(
                $"SqrtFloor needs an operand of twice the root words. operand: {operand.NumWords}, root: {n} words.");
        }
    }

    // r^2, then SqrtFloorCorrection moves root, within a few steps of floor(sqrt(operand)), to it on the device
    private static void CorrectSqrtFloor(GpuBigUInt operand, GpuBigUInt root)
    {
        var workspace = operand.Workspace;
        var n = root.NumWords;
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
    /// Computes the integer square root ($\lfloor\sqrt{\text{leftOperand}}\rfloor$) on the GPU.
    /// </summary>
    /// <param name="leftOperand">The non-negative big integer operand.</param>
    /// <param name="resultBuffer">The destination buffer receiving the integer square root. It may be the operand, or
    /// overlap it: every path reads the operand completely before it writes the result.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than half the bits of <paramref
    /// name="leftOperand"/>, rounded up to words.</exception>
    /// <remarks>
    /// <para><b>Algorithm:</b> Shifts the operand left by an even number of bits so that its root fills whole words,
    /// takes that root with an inverse square root and an exact correction, and shifts it back: exactly the floor. The
    /// work is queued on the workspace stream; only the operand's bit length is read back to the host first.</para>
    /// <para>A root wider than <see cref="GpuWorkspace.SqrtPrecisionDoublingThresholdWords"/> words takes a
    /// half-precision inverse square root by precision doubling driven from the host and one Karp-Markstein step; the
    /// result is the same.</para>
    /// </remarks>
    public static void Sqrt(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView resultBuffer) =>
        SqrtCore(leftOperand.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt());

    // Sqrt on the owned type; the public method above takes views and borrows them
    private static void SqrtCore(GpuBigUInt leftOperand, GpuBigUInt resultBuffer)
    {
        var words = leftOperand.NumWords;
        ResultBuffers.ThrowIfNarrower(resultBuffer, BigIntWidths.Words(BigIntWidths.Sqrt(BigIntWidths.OfWords(words))),
            nameof(Sqrt));
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

        SqrtThroughSqrtFloor(leftOperand, resultBuffer, actualBits);
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

    // An operand above 32 bits, shifted left by 2k bits to the bit length 64r or 64r - 1 that SqrtFloor takes; its
    // root is shifted back right by k. The operand is read only by the first shift and the result written only by the
    // last, so the result may be the operand.
    private static void SqrtThroughSqrtFloor(GpuBigUInt leftOperand, GpuBigUInt resultBuffer, int actualBits)
    {
        var workspace = leftOperand.Workspace;
        var rootWords = (actualBits + 63) / 64;
        var halfShift = (64 * rootWords - actualBits) / 2;

        using var normalized = workspace.RentTemp(2 * rootWords);
        // actualBits + 2k <= 64r = 32 * 2r: the shift moves no set bit out of the 2r words
        GpuBigIntMath.ShiftAndResize(leftOperand, 2 * halfShift, normalized);

        using var scaledRoot = workspace.RentTemp(rootWords);
        SqrtFloor(normalized, scaledRoot);

        // scaledRoot = floor(sqrt(a * 2^(2k))) = floor(sqrt(a) * 2^k), so floor(scaledRoot / 2^k) = floor(sqrt(a));
        // the shift writes every word of the result, zeros above the root
        GpuBigIntMath.ShiftAndResize(scaledRoot, -halfShift, resultBuffer);
    }
}
