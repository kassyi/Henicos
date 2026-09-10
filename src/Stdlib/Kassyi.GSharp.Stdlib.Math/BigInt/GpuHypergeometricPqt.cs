using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents the intermediate state (P, Q, T) of a hypergeometric series evaluated using Binary Splitting on the GPU.
/// </summary>
public readonly struct GpuHypergeometricPqt : IDisposable
{
    public SignedGpuBigInteger P { get; }
    public GpuBigInteger Q { get; }
    public SignedGpuBigInteger T { get; }

    public GpuHypergeometricPqt(SignedGpuBigInteger p, GpuBigInteger q, SignedGpuBigInteger t)
    {
        P = p;
        Q = q ?? throw new ArgumentNullException(nameof(q));
        T = t;
    }

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
        var resultP = new GpuBigInteger(context, pWords);
        GpuBigIntMath.Mul(left.P.Value, right.P.Value, resultP);
        var pNegative = left.P.IsNegative ^ right.P.IsNegative;

        // Q = Ql * Qr
        var qWords = left.Q.NumWords + right.Q.NumWords;
        var resultQ = new GpuBigInteger(context, qWords);
        GpuBigIntMath.Mul(left.Q, right.Q, resultQ);

        // T = Tl * Qr + Pl * Tr
        var wordsA = left.T.Value.NumWords + right.Q.NumWords;
        using var rawTermA = new GpuBigInteger(context, wordsA);
        GpuBigIntMath.Mul(left.T.Value, right.Q, rawTermA);
        var signA = left.T.IsNegative;

        var wordsB = left.P.Value.NumWords + right.T.Value.NumWords;
        using var rawTermB = new GpuBigInteger(context, wordsB);
        GpuBigIntMath.Mul(left.P.Value, right.T.Value, rawTermB);
        var signB = left.P.IsNegative ^ right.T.IsNegative;

        var maxTWords = System.Math.Max(wordsA, wordsB);
        using var termA = rawTermA.PadTo(maxTWords);
        using var termB = rawTermB.PadTo(maxTWords);
        var resultT = new GpuBigInteger(context, maxTWords);

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
            new SignedGpuBigInteger(resultP, pNegative),
            resultQ,
            new SignedGpuBigInteger(resultT, tNegative)
        );
    }
}
