using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents the intermediate state (P, Q, T) of a hypergeometric series evaluated using Binary Splitting on the GPU.
/// </summary>
/// <remarks>
/// Defined according to the Binary Splitting algorithm:
/// <list type="bullet">
///   <item><description><see cref="P"/>: <c>P(a, b) = \prod p(k)</c></description></item>
///   <item><description><see cref="Q"/>: <c>Q(a, b) = \prod q(k)</c></description></item>
///   <item><description><see cref="T"/>: <c>T(a, b) = \sum ...</c></description></item>
/// </list>
/// </remarks>
public readonly struct GpuHypergeometricPqt(GpuBigInteger p, GpuUIntBigInteger q, GpuBigInteger t) : IDisposable
{
    public GpuBigInteger P { get; } = p;
    public GpuUIntBigInteger Q { get; } = q ?? throw new ArgumentNullException(nameof(q));
    public GpuBigInteger T { get; } = t;

    public void Dispose()
    {
        P.Dispose();
        Q.Dispose();
        T.Dispose();
    }

    /// <summary>
    /// Merges two adjacent <see cref="GpuHypergeometricPqt"/> states on the GPU.
    /// P = Pl * Pr
    /// Q = Ql * Qr
    /// T = Tl * Qr + Pl * Tr
    /// </summary>
    public static GpuHypergeometricPqt Merge(GpuContext context, GpuHypergeometricPqt left, GpuHypergeometricPqt right)
    {
        // P = Pl * Pr
        var pWords = left.P.Value.NumWords + right.P.Value.NumWords;
        var resultP = new GpuUIntBigInteger(context, pWords);
        GpuBigIntMath.Mul(left.P.Value, right.P.Value, resultP);
        var pNegative = left.P.IsNegative ^ right.P.IsNegative;

        // Q = Ql * Qr
        var qWords = left.Q.NumWords + right.Q.NumWords;
        var resultQ = new GpuUIntBigInteger(context, qWords);
        GpuBigIntMath.Mul(left.Q, right.Q, resultQ);

        // T = Tl * Qr + Pl * Tr
        var wordsA = left.T.Value.NumWords + right.Q.NumWords;
        using var rawTermA = new GpuUIntBigInteger(context, wordsA);
        GpuBigIntMath.Mul(left.T.Value, right.Q, rawTermA);
        var signA = left.T.IsNegative;

        var wordsB = left.P.Value.NumWords + right.T.Value.NumWords;
        using var rawTermB = new GpuUIntBigInteger(context, wordsB);
        GpuBigIntMath.Mul(left.P.Value, right.T.Value, rawTermB);
        var signB = left.P.IsNegative ^ right.T.IsNegative;

        var maxTWords = System.Math.Max(wordsA, wordsB);
        using var termA = rawTermA.PadTo(maxTWords);
        using var termB = rawTermB.PadTo(maxTWords);
        var resultT = new GpuUIntBigInteger(context, maxTWords);

        bool tNegative;
        if (signA == signB)
        {
            GpuBigIntMath.Add(termA, termB, resultT);
            tNegative = signA;
        }
        else
        {
            var comparison = termA.CompareTo(termB);
            if (comparison >= 0)
            {
                GpuBigIntMath.Sub(termA, termB, resultT);
                tNegative = signA;
            }
            else
            {
                GpuBigIntMath.Sub(termB, termA, resultT);
                tNegative = signB;
            }
        }

        return new GpuHypergeometricPqt(
            new GpuBigInteger(resultP, pNegative),
            resultQ,
            new GpuBigInteger(resultT, tNegative)
        );
    }
}
