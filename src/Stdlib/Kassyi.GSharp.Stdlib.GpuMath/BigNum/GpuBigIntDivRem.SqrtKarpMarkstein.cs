namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// The square root of SqrtFloor for roots wider than GpuWorkspace.SqrtPrecisionDoublingThresholdWords: an inverse
// square root at half the precision by precision doubling, a half-precision root and one Karp-Markstein step that
// lands on floor(sqrt(a)) or one below; the derivation is in docs/spec/en/02_architecture/04_bignum_internals.md.
public static partial class GpuBigIntDivRem
{
    /// <summary>
    /// The values of the Karp-Markstein square root, as <see cref="SqrtFloorKarpMarksteinForTest"/> reads them back.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description><see cref="Stages"/>: the precision doubling up to <c>p_y = max(3, floor(n/2) + 1)</c>
    ///   words.</description></item>
    ///   <item><description><see cref="Y"/>: <c>y = Y - 2</c>, <c>0 &lt;= floor(2^P / sqrt(A)) - y &lt;= 3</c>,
    ///   <c>P = 16n + 14 + 32 p_y</c>, <c>A</c> the top <c>n + 1</c> words of the operand.</description></item>
    ///   <item><description><see cref="S0"/>: <c>s0 = floor(A_t·y / 2^(P - 32j))</c>, <c>A_t = floor(A / 2^(32j))</c>,
    ///   <c>j = floor(n/2)</c>.</description></item>
    ///   <item><description><see cref="DHigh"/>: <c>d_hi = A - s0^2</c>.</description></item>
    ///   <item><description><see cref="DShifted"/>: <c>d' = floor(d_hi / 2^31)</c>.</description></item>
    ///   <item><description><see cref="Delta"/>: <c>delta = floor(y·d' / 2^(32 p_y))</c>.</description></item>
    ///   <item><description><see cref="Estimate"/>: <c>r'' = s0·2^(16n - 16) + delta</c>, before the
    ///   correction.</description></item>
    /// </list>
    /// </remarks>
    internal sealed class SqrtKarpMarksteinTrace
    {
        /// <summary>The precision doubling stages up to <c>p_y = max(3, floor(n/2) + 1)</c> words.</summary>
        public List<InverseSqrtDoublingStage> Stages { get; } = [];

        /// <summary><c>y = Y - 2</c>, <c>0 &lt;= floor(2^P / sqrt(A)) - y &lt;= 3</c>.</summary>
        public BigInteger Y { get; set; }

        /// <summary><c>s0 = floor(A_t·y / 2^(P - 32j))</c>, <c>A_t = floor(A / 2^(32j))</c>.</summary>
        public BigInteger S0 { get; set; }

        /// <summary><c>d_hi = A - s0^2</c>.</summary>
        public BigInteger DHigh { get; set; }

        /// <summary><c>d' = floor(d_hi / 2^31)</c>.</summary>
        public BigInteger DShifted { get; set; }

        /// <summary><c>delta = floor(y·d' / 2^(32 p_y))</c>.</summary>
        public BigInteger Delta { get; set; }

        /// <summary><c>r'' = s0·2^(16n - 16) + delta</c>, before the correction.</summary>
        public BigInteger Estimate { get; set; }
    }

    /// <summary>
    /// Test only: <see cref="SqrtFloor"/> by the Karp-Markstein step whatever the threshold, reading every
    /// intermediate value back to the host. <paramref name="root"/> receives <c>floor(sqrt(a))</c>.
    /// </summary>
    /// <param name="operand">The operand <c>a</c>, of <c>2 * root.NumWords</c> words and bit length <c>64n</c> or
    /// <c>64n - 1</c>.</param>
    /// <param name="root">Receives <c>floor(sqrt(a))</c> (<c>n</c> words).</param>
    /// <param name="yOffset">Optional test-only offset added to <c>y = Y - 2</c> to exercise domain-error
    /// checks.</param>
    /// <returns>The recorded stages and intermediate values of the Karp-Markstein step.</returns>
    /// <exception cref="ArgumentException"><paramref name="operand"/> does not have twice the words of
    /// <paramref name="root"/>, or its bit length is below <c>64n - 1</c>.</exception>
    internal static SqrtKarpMarksteinTrace SqrtFloorKarpMarksteinForTest(GpuBigUInt operand, GpuBigUInt root,
        uint yOffset = 0)
    {
        ThrowIfNotTwiceTheRoot(operand, root);
        var trace = new SqrtKarpMarksteinTrace();
        SqrtFloorKarpMarkstein(operand, root, trace, yOffset);
        return trace;
    }

    // floor(sqrt(a)) into root, n words, for a of 2n words with bit length 64n or 64n - 1: y ~ 2^P / sqrt(A) from the
    // precision doubling stopped at p_y words, s = s0·2^(16n - 16) ~ sqrt(a) to half the bits, then r'' = s + delta,
    // floor(sqrt(a)) or one below, and the device correction
    private static void SqrtFloorKarpMarkstein(GpuBigUInt operand, GpuBigUInt root, SqrtKarpMarksteinTrace? trace,
        uint yOffset = 0)
    {
        var w = new KarpMarksteinWidths(root.NumWords);
        var a = operand.BorrowWords(w.RootWords - 1, w.OperandWords);

        using var y = ComputeHalfPrecisionInverse(a, w, trace?.Stages, yOffset);
        ComputeHalfRootAndResidual(a, y, w, out var halfRoot, out var residual);
        using (halfRoot)
        using (residual)
        {
            ApplyKarpMarksteinStep(y, halfRoot, residual, root, w, out var scaledResidual, out var stepProduct);
            using (scaledResidual)
            using (stepProduct)
            {
                if (trace is not null)
                {
                    RecordKarpMarksteinTrace(trace, y, halfRoot, residual, scaledResidual,
                        stepProduct.BorrowWords(w.InverseWords, w.StepWords), root);
                }
            }
        }

        CorrectSqrtFloor(operand, root);
    }

    // y = Y - 2 with Y of the last stage: 0 <= floor(2^P / sqrt(A)) - y <= 3, never above the root (one host sync).
    private static GpuBigUInt ComputeHalfPrecisionInverse(GpuBigUInt a, in KarpMarksteinWidths w,
        List<InverseSqrtDoublingStage>? stages, uint yOffset)
    {
        var firstY = InitialGuessSquareRootFromLeadingBits(a, 16 * w.OperandWords + 62, out var actualBits);
        if (actualBits < 32 * w.OperandWords - 1)
        {
            throw new ArgumentException(
                $"SqrtFloor needs a normalized operand of bit length {64 * w.RootWords} or {64 * w.RootWords - 1}. "
                + $"top words bit length: {actualBits}, operand words: {w.OperandWords}.");
        }

        var y = a.Workspace.RentTemp(w.InverseWords);
        try
        {
            WriteInverseSqrtDoublingFirstY(y, firstY);
            RunInverseSqrtDoublingStages(a, y, stages);
            GpuBigIntMath.Sub(y, 2u, y);
            if (yOffset != 0)
            {
                GpuBigIntMath.Add(y, yOffset, y);
            }

            return y;
        }
        catch
        {
            y.Dispose();
            throw;
        }
    }

    // s0 = floor(A_t·y / 2^(P - 32j)) and d_hi = A - s0^2 (checked: non-negative and within residualWords).
    private static void ComputeHalfRootAndResidual(GpuBigUInt a, GpuBigUInt y, in KarpMarksteinWidths w,
        out GpuBigUInt halfRoot, out GpuBigUInt residual)
    {
        var workspace = a.Workspace;
        using var halfRootProduct = workspace.RentTemp(w.OperandWords - w.DroppedWords + w.InverseWords);
        GpuBigIntMath.Mul(a.BorrowWords(w.DroppedWords, w.OperandWords - w.DroppedWords), y, halfRootProduct);

        halfRoot = workspace.RentTemp(w.HalfRootWords);
        residual = workspace.RentTemp(w.ResidualWords);
        try
        {
            GpuBigIntMath.ShiftAndResize(halfRootProduct, -(w.InverseScale - 32 * w.DroppedWords), halfRoot);

            using var halfRootSquared = workspace.RentTemp(2 * w.HalfRootWords);
            GpuBigIntMath.Mul(halfRoot, halfRoot, halfRootSquared);
            BigIntWideOps.AddSubtract(a, halfRootSquared, residual, subtract: true);
#if DEBUG
            // Debug builds check that d' = d_hi >> 31 fits stepWords words, as the derivation proves
            BigIntWideOps.CheckFitsBits(residual, 32L * w.StepWords + 31);
#endif
        }
        catch
        {
            residual.Dispose();
            halfRoot.Dispose();
            throw;
        }
    }

    // d' = floor(d_hi / 2^31), delta = floor(y·d' / 2^(32 p_y)), r'' = s0·2^(16n - 16) + delta (checked).
    private static void ApplyKarpMarksteinStep(GpuBigUInt y, GpuBigUInt halfRoot, GpuBigUInt residual, GpuBigUInt root,
        in KarpMarksteinWidths w, out GpuBigUInt scaledResidual, out GpuBigUInt stepProduct)
    {
        var workspace = y.Workspace;
        scaledResidual = workspace.RentTemp(w.StepWords);
        stepProduct = workspace.RentTemp(w.InverseWords + w.StepWords);
        try
        {
            GpuBigIntMath.ShiftAndResize(residual, -31, scaledResidual);
            GpuBigIntMath.Mul(y, scaledResidual, stepProduct);
            var step = stepProduct.BorrowWords(w.InverseWords, w.StepWords);

            GpuBigIntMath.ShiftAndResize(halfRoot, w.RootShift, root);
            BigIntWideOps.AddSubtract(root, step, root, subtract: false);
        }
        catch
        {
            stepProduct.Dispose();
            scaledResidual.Dispose();
            throw;
        }
    }

    private static void RecordKarpMarksteinTrace(SqrtKarpMarksteinTrace trace, GpuBigUInt y, GpuBigUInt halfRoot,
        GpuBigUInt residual, GpuBigUInt scaledResidual, GpuBigUInt step, GpuBigUInt root)
    {
        trace.Y = y.ToBigInteger();
        trace.S0 = halfRoot.ToBigInteger();
        trace.DHigh = residual.ToBigInteger();
        trace.DShifted = scaledResidual.ToBigInteger();
        trace.Delta = step.ToBigInteger();
        trace.Estimate = root.ToBigInteger();
    }

    private readonly record struct KarpMarksteinWidths(int RootWords)
    {
        // w: A = floor(a / 2^(32(n - 1)))
        public int OperandWords => RootWords + 1;

        // p_y; a stage of at least 3 words has the 5/4 bound
        public int InverseWords => Math.Max(3, RootWords / 2 + 1);

        // j: A_t = floor(A / 2^(32j)) costs s0 at most 1 + 2^-15
        public int DroppedWords => RootWords / 2;

        // s0 <= sqrt(A) < 2^(16n + 16)
        public int HalfRootWords => RootWords / 2 + 1;

        // d_hi < 2^(16n + 24)
        public int ResidualWords => (RootWords + 1) / 2 + 1;

        // d' and delta < 2^(16n - 7)
        public int StepWords => (RootWords + 1) / 2;

        // P: y ~ 2^P / sqrt(A)
        public int InverseScale => 16 * RootWords + 14 + 32 * InverseWords;

        // 16n - 16: s = s0·2^(16n - 16)
        public int RootShift => 16 * RootWords - 16;
    }
}
