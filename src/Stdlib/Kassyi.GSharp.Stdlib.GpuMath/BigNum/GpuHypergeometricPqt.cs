namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents the intermediate tuple state $(P, Q, T)$ of a hypergeometric series evaluated using Binary Splitting on the GPU.
/// </summary>
/// <param name="p">The cumulative $P$ product component. Takes ownership of the buffer.</param>
/// <param name="q">The cumulative $Q$ product component. Takes ownership of the buffer.</param>
/// <param name="t">The cumulative $T$ partial sum component. Takes ownership of the buffer.</param>
/// <remarks>
/// <para>
/// Defined according to the Binary Splitting algorithm for evaluating series of the form $\sum \frac{a(k) \cdot p(0) \cdots p(k)}{b(k) \cdot q(0) \cdots q(k)}$:
/// <list type="bullet">
///   <item><description><see cref="P"/>: $P(a, b) = \prod_{k=a}^{b-1} p(k)$</description></item>
///   <item><description><see cref="Q"/>: $Q(a, b) = \prod_{k=a}^{b-1} q(k)$</description></item>
///   <item><description><see cref="T"/>: $T(a, b) = \sum_{k=a}^{b-1} a(k) p(a)\cdots p(k) q(k+1)\cdots q(b-1)$</description></item>
/// </list>
/// </para>
/// <para><b>Memory Management:</b> Implements <see cref="IDisposable"/> to release underlying GPU memory allocated for <see cref="P"/>, <see cref="Q"/>, and <see cref="T"/>.</para>
/// <para><b>Thread Safety:</b> Value-type wrapper semantics; distinct instances are safe for concurrent read operations.</para>
/// </remarks>
public readonly struct GpuHypergeometricPqt([TakesOwnership] GpuBigInt p, [TakesOwnership] GpuBigUInt q, [TakesOwnership] GpuBigInt t) : IDisposable
{
    /// <summary>
    /// Gets the cumulative product term $P(a, b)$.
    /// </summary>
    public GpuBigInt P { get; } = p;

    /// <summary>
    /// Gets the cumulative product term $Q(a, b)$.
    /// </summary>
    public GpuBigUInt Q { get; } = q;

    /// <summary>
    /// Gets the cumulative summation term $T(a, b)$.
    /// </summary>
    public GpuBigInt T { get; } = t;

    /// <summary>
    /// Releases the GPU global device memory resources allocated for <see cref="P"/>, <see cref="Q"/>, and <see cref="T"/>.
    /// </summary>
    public void Dispose()
    {
        P.Dispose();
        Q.Dispose();
        T.Dispose();
    }

    /// <summary>
    /// Merges two adjacent <see cref="GpuHypergeometricPqt"/> states $(P_l, Q_l, T_l)$ and $(P_r, Q_r, T_r)$ on the GPU.
    /// </summary>
    /// <param name="workspace">The parent GPU execution context managing memory and kernel execution.</param>
    /// <param name="left">The left child binary splitting state $(P_l, Q_l, T_l)$.</param>
    /// <param name="right">The right child binary splitting state $(P_r, Q_r, T_r)$.</param>
    /// <returns>A new <see cref="GpuHypergeometricPqt"/> state where $P = P_l P_r$, $Q = Q_l Q_r$, and $T = T_l Q_r + P_l T_r$.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Dispatches high-throughput GPU multiplication and signed addition kernels.</para>
    /// </remarks>
    public static GpuHypergeometricPqt Merge(GpuWorkspace workspace, GpuHypergeometricPqt left, GpuHypergeometricPqt right)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        // P = Pl * Pr
        var pWords = left.P.Value.NumWords + right.P.Value.NumWords;
        var resultP = new GpuBigUInt(workspace, pWords);
        GpuBigIntMath.Mul(left.P.Value, right.P.Value, resultP);
        var pNegative = left.P.IsNegative ^ right.P.IsNegative;

        // Q = Ql * Qr
        var qWords = left.Q.NumWords + right.Q.NumWords;
        var resultQ = new GpuBigUInt(workspace, qWords);
        GpuBigIntMath.Mul(left.Q, right.Q, resultQ);

        // T = Tl * Qr + Pl * Tr
        var wordsA = left.T.Value.NumWords + right.Q.NumWords;
        using var rawTermA = new GpuBigUInt(workspace, wordsA);
        GpuBigIntMath.Mul(left.T.Value, right.Q, rawTermA);
        var signA = left.T.IsNegative;

        var wordsB = left.P.Value.NumWords + right.T.Value.NumWords;
        using var rawTermB = new GpuBigUInt(workspace, wordsB);
        GpuBigIntMath.Mul(left.P.Value, right.T.Value, rawTermB);
        var signB = left.P.IsNegative ^ right.T.IsNegative;

        var maxTWords = Math.Max(wordsA, wordsB);
        using var termA = rawTermA.PadTo(maxTWords);
        using var termB = rawTermB.PadTo(maxTWords);
        var resultT = new GpuBigUInt(workspace, maxTWords);

        var tNegative = GpuBigIntMath.AddSignedMagnitude(termA, signA, termB, signB, resultT);

        return new GpuHypergeometricPqt(
            new GpuBigInt(resultP, pNegative),
            resultQ,
            new GpuBigInt(resultT, tNegative)
        );
    }
}

