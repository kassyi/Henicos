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
    /// <returns>The recorded stages and intermediate values of the Karp-Markstein step.</returns>
    /// <exception cref="ArgumentException"><paramref name="operand"/> does not have twice the words of
    /// <paramref name="root"/>.</exception>
    internal static SqrtKarpMarksteinTrace SqrtFloorKarpMarksteinForTest(GpuBigUInt operand, GpuBigUInt root)
    {
        ThrowIfNotTwiceTheRoot(operand, root);
        var trace = new SqrtKarpMarksteinTrace();
        SqrtFloorKarpMarkstein(operand, root, trace);
        return trace;
    }

    // floor(sqrt(a)) into root, n words, for a of 2n words with bit length 64n or 64n - 1: y ~ 2^P / sqrt(A) from the
    // precision doubling stopped at p_y words, s = s0·2^(16n - 16) ~ sqrt(a) to half the bits, then r'' = s + delta,
    // floor(sqrt(a)) or one below, and the device correction
    private static void SqrtFloorKarpMarkstein(GpuBigUInt operand, GpuBigUInt root, SqrtKarpMarksteinTrace? trace)
    {
        var workspace = operand.Workspace;
        var n = root.NumWords;
        var operandWords = n + 1;                           // w: A = floor(a / 2^(32(n - 1)))
        var inverseWords = Math.Max(3, n / 2 + 1);          // p_y; a stage of at least 3 words has the 5/4 bound
        var droppedWords = n / 2;                           // j: A_t = floor(A / 2^(32j)) costs s0 at most 1 + 2^-15
        var halfRootWords = n / 2 + 1;                      // s0 <= sqrt(A) < 2^(16n + 16)
        var residualWords = (n + 1) / 2 + 1;                // d_hi < 2^(16n + 24)
        var stepWords = (n + 1) / 2;                        // d' and delta < 2^(16n - 7)
        var inverseScale = 16 * n + 14 + 32 * inverseWords; // P: y ~ 2^P / sqrt(A)

        var a = operand.BorrowWords(n - 1, operandWords);

        // y = Y - 2 with Y of the last stage: 0 <= floor(2^P / sqrt(A)) - y <= 3, never above the root
        using var y = workspace.RentTemp(inverseWords);
        WriteInverseSqrtDoublingFirstY(y, InitialGuessValue(a, 16 * operandWords + 62, isSquareRoot: true));
        RunInverseSqrtDoublingStages(a, y, trace?.Stages);
        GpuBigIntMath.Sub(y, 2u, y);

        // s0 = floor(A_t·y / 2^(P - 32j))
        using var halfRootProduct = workspace.RentTemp(operandWords - droppedWords + inverseWords);
        GpuBigIntMath.Mul(a.BorrowWords(droppedWords, operandWords - droppedWords), y, halfRootProduct);
        using var halfRoot = workspace.RentTemp(halfRootWords);
        GpuBigIntMath.ShiftAndResize(halfRootProduct, -(inverseScale - 32 * droppedWords), halfRoot);

        // d_hi = A - s0^2, the top words of a - s^2; checked: non-negative and within residualWords
        using var halfRootSquared = workspace.RentTemp(2 * halfRootWords);
        GpuBigIntMath.Mul(halfRoot, halfRoot, halfRootSquared);
        using var residual = workspace.RentTemp(residualWords);
        BigIntWideOps.AddSubtract(a, halfRootSquared, residual, subtract: true);
#if DEBUG
        // Debug builds check that d' = d_hi >> 31 fits stepWords words, as the derivation proves
        BigIntWideOps.CheckFitsBits(residual, 32L * stepWords + 31);
#endif

        // d' = floor(d_hi / 2^31) = floor((a - s^2) / 2^(32n - 1)); delta = floor(y·d' / 2^(32 p_y)), the top words
        using var scaledResidual = workspace.RentTemp(stepWords);
        GpuBigIntMath.ShiftAndResize(residual, -31, scaledResidual);
        using var stepProduct = workspace.RentTemp(inverseWords + stepWords);
        GpuBigIntMath.Mul(y, scaledResidual, stepProduct);
        var step = stepProduct.BorrowWords(inverseWords, stepWords);

        // r'' = s0·2^(16n - 16) + delta: floor(sqrt(a)) or one below; checked: fits the n words of root
        GpuBigIntMath.ShiftAndResize(halfRoot, 16 * n - 16, root);
        BigIntWideOps.AddSubtract(root, step, root, subtract: false);

        if (trace is not null)
        {
            trace.Y = y.ToBigInteger();
            trace.S0 = halfRoot.ToBigInteger();
            trace.DHigh = residual.ToBigInteger();
            trace.DShifted = scaledResidual.ToBigInteger();
            trace.Delta = step.ToBigInteger();
            trace.Estimate = root.ToBigInteger();
        }

        CorrectSqrtFloor(operand, root);
    }
}
