namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// GPU implementation of the big-float transcendental functions of <see cref="GSharpKernelFloatProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm and every truncation match the host implementation <see cref="BigFloatTranscendentalMath"/>, which
/// documents the mathematics. The term counts and precisions come from <see cref="TranscendentalPlanner"/> before
/// any device work, so the series run without reading values back; the host waits for the device only to read the
/// leading bits of the argument, the bit length of the reduced argument, inside the division, and to normalize.
/// </para>
/// <para>
/// Fixed-point values with <c>W</c> fraction bits occupy <c>W / 32</c> words, plus one integer word where the value
/// may reach 1. The top part of a product is a view of the product buffer, not a copy.
/// </para>
/// </remarks>
internal static class GpuBigFloatTranscendentals
{
    /// <summary>
    /// Computes <c>ln x</c> for a positive operand on the GPU (see <see cref="BigFloatTranscendentalMath.Log"/>).
    /// </summary>
    /// <param name="workspace">The workspace whose stream orders the work.</param>
    /// <param name="value">The operand; its sign is ignored (the caller rejects negative values).</param>
    /// <param name="resultSig">The result significand; its word count sets the precision.</param>
    /// <returns>The exponent and sign of the result.</returns>
    /// <exception cref="InvalidOperationException">The operand is zero.</exception>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/>.</exception>
    internal static GpuFloatResult Log(GpuWorkspace workspace, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig)
    {
        var provider = workspace.Provider;
        var source = value.Significand.AsBorrowedBigUInt(workspace);
        provider.ReadLeadingBits(workspace, source, out var bitLength, out var leadingBits);
        if (bitLength == 0)
        {
            throw new InvalidOperationException("Log of zero.");
        }

        var top = (long)value.Exponent + bitLength;
        var upper = bitLength >= 2 && leadingBits >> 62 == 3;
        var e = upper ? top : top - 1;

        // |m - 1| = U / 2^b (upper) or U / 2^(b-1) (lower), |y| = U / D; D < 2^(b+1) fits b / 32 + 1 words.
        var spanWords = bitLength / 32 + 1;
        using var reduced = workspace.RentTemp(spanWords);
        using var denominator = workspace.RentTemp(spanWords);
        BuildReducedRatio(workspace, source, upper ? bitLength : bitLength - 1, upper, reduced, denominator);
        var reducedBits = reduced.GetBitLength();
        var result = resultSig.AsBorrowedBigUInt(workspace);
        if (reducedBits == 0 && e == 0)
        {
            provider.ClearMemoryAsync(result.DevicePointer, result.NumWords, workspace.Stream);
            return new GpuFloatResult(0, false);
        }

        var closeness = reducedBits == 0
            ? int.MaxValue
            : upper ? bitLength - reducedBits : bitLength - reducedBits - 1;
        var plan = TranscendentalPlanner.PlanLog(32 * resultSig.NumWords, e, closeness);
        var ratio = new ReducedRatio(reduced, denominator, bitLength, reducedBits);

        using var approximation = workspace.RentTemp(plan.WorkingBits / 32);
        long approximationExponent;
        bool negative;
        if (e == 0)
        {
            approximationExponent = NormalizeSeries(workspace, ratio, plan, approximation);
            negative = upper;
        }
        else
        {
            approximationExponent = SumWithLn2(workspace, ratio, plan, e, upper, approximation);
            negative = e < 0;
        }

        return RoundToNearest(
            workspace, approximation, approximationExponent, plan.WorkingBits - plan.ResultBits, result, negative);
    }

    // U = |S - 2^k| and D = S + 2^k (k = b - 1 below 1.5 · 2^(b-1), k = b at or above it).
    private static void BuildReducedRatio(
        GpuWorkspace workspace, GpuBigUInt source, int powerBit, bool upper, GpuBigUInt reduced, GpuBigUInt denominator)
    {
        var provider = workspace.Provider;
        var stream = workspace.Stream;
        var words = reduced.NumWords;
        using var padded = workspace.RentTemp(words);
        using var power = workspace.RentTemp(words);
        provider.ClearMemoryAsync(padded.DevicePointer, words, stream);
        provider.CopyDeviceToDevice(padded.DevicePointer, source.DevicePointer, Math.Min(source.NumWords, words), stream);
        provider.ClearMemoryAsync(power.DevicePointer, words, stream);
        provider.WriteWord(power.DevicePointer, powerBit / 32, 1u << (powerBit % 32), stream);
        if (upper)
        {
            GpuBigIntMath.Sub(power, padded, reduced);
        }
        else
        {
            GpuBigIntMath.Sub(padded, power, reduced);
        }

        GpuBigIntMath.Add(padded, power, denominator);
    }

    // e = 0: ln m = ±2|y|T, normalized to W bits. Returns the exponent of the approximation.
    private static long NormalizeSeries(GpuWorkspace workspace, in ReducedRatio ratio, in LogPlan plan, GpuBigUInt approximation)
    {
        var workingWords = plan.WorkingBits / 32;
        using var series = workspace.RentTemp(2 * workingWords + 2);
        var seriesExponent = EvaluateSeries(workspace, ratio, plan, series);
        var shift = GpuBigIntMath.NormalizeToMsb(series, approximation, out _);
        return seriesExponent - shift;
    }

    // e != 0: |e|·ln 2 ± 2|y|T in fixed point with F = W + 2 fraction bits, normalized to W bits.
    private static long SumWithLn2(
        GpuWorkspace workspace, in ReducedRatio ratio, in LogPlan plan, long e, bool upper, GpuBigUInt approximation)
    {
        var workingWords = plan.WorkingBits / 32;
        var fractionBits = plan.WorkingBits + 2;

        // |e·ln 2| < 2^32 and the sum carries 2 more fraction bits: W + 2 + 33 bits.
        var sumWords = workingWords + 3;
        using var eLn2 = workspace.RentTemp(sumWords);
        MultiplyLn2(workspace, e, plan.Ln2FractionBits, fractionBits, eLn2);
        if (ratio.ReducedBits == 0)
        {
            return -fractionBits - GpuBigIntMath.NormalizeToMsb(eLn2, approximation, out _);
        }

        using var series = workspace.RentTemp(2 * workingWords + 2);
        var seriesExponent = EvaluateSeries(workspace, ratio, plan, series);

        // 2|y|T · 2^F; the series exponent 1 - sh - W is below -F.
        using var aligned = workspace.RentTemp(sumWords);
        GpuBigIntMath.ShiftAndResize(series, (int)(seriesExponent + fractionBits), aligned);
        using var sum = workspace.RentTemp(sumWords);
        if (upper == (e < 0))
        {
            GpuBigIntMath.Add(eLn2, aligned, sum);
        }
        else
        {
            GpuBigIntMath.Sub(eLn2, aligned, sum);
        }

        return -fractionBits - GpuBigIntMath.NormalizeToMsb(sum, approximation, out _);
    }

    // 2|y|T as an integer in `series` (2W/32 + 2 words); returns its exponent 1 - sh - W.
    private static long EvaluateSeries(GpuWorkspace workspace, in ReducedRatio ratio, in LogPlan plan, GpuBigUInt series)
    {
        var workingBits = plan.WorkingBits;
        var workingWords = workingBits / 32;

        // Y = floor(U · 2^sh / D) in [2^(W-1), 2^(W+1)): U · 2^sh has W + b + 1 bits.
        var shift = checked(workingBits + ratio.BitLength - ratio.ReducedBits + 1);
        var numeratorWords = checked(workingBits + ratio.BitLength + 1 + 31) / 32;
        using var numerator = workspace.RentTemp(numeratorWords);
        GpuBigIntMath.ShiftAndResize(ratio.Reduced, shift, numerator);
        using var quotient = workspace.RentTemp(numeratorWords);
        using var remainder = workspace.RentTemp(ratio.Denominator.NumWords);
        GpuBigIntDivRem.DivRem(numerator, ratio.Denominator, quotient, remainder);
        var y = Slice(workspace, quotient, 0, workingWords + 1);

        // Z = floor(Y² · 2^(W - 2sh)) = z in W fraction bits (z <= 1/25).
        using var ySquared = workspace.RentTemp(2 * workingWords + 2);
        GpuBigIntMath.Mul(y, y, ySquared);
        using var z = workspace.RentTemp(workingWords);
        GpuBigIntMath.ShiftAndResize(ySquared, workingBits - 2 * shift, z);

        using var t = workspace.RentTemp(workingWords + 1);
        AtanhSeriesHorner(workspace, z, plan.Terms, t);
        GpuBigIntMath.Mul(y, t, series);
        return 1L - shift - workingBits;
    }

    // H = floor(2^W / (2N - 1)); H = floor(H · Z / 2^W) + floor(2^W / (2k + 1)) for k = N - 2 down to 0. `h` has
    // W / 32 + 1 words.
    private static void AtanhSeriesHorner(GpuWorkspace workspace, GpuBigUInt z, int terms, GpuBigUInt h)
    {
        var provider = workspace.Provider;
        var stream = workspace.Stream;
        var workingWords = z.NumWords;
        var words = workingWords + 1;
        using var one = workspace.RentTemp(words);
        provider.ClearMemoryAsync(one.DevicePointer, words, stream);
        provider.WriteWord(one.DevicePointer, workingWords, 1u, stream);
        using var coefficient = workspace.RentTemp(words);
        using var product = workspace.RentTemp(2 * workingWords + 1);
        using var discardedRemainder = workspace.RentTemp(1);

        DivideByUInt32Async(workspace, one, (uint)(2 * terms - 1), h, discardedRemainder);
        var high = Slice(workspace, product, workingWords, words);
        for (var k = terms - 2; k >= 0; k--)
        {
            GpuBigIntMath.Mul(h, z, product);
            DivideByUInt32Async(workspace, one, (uint)(2 * k + 1), coefficient, discardedRemainder);
            GpuBigIntMath.Add(high, coefficient, h);
        }
    }

    // floor(|e| · L / 2^(Lbits - F)) into `target`, L = floor(ln 2 · 2^Lbits) from the workspace constants.
    private static void MultiplyLn2(GpuWorkspace workspace, long e, int ln2FractionBits, int fractionBits, GpuBigUInt target)
    {
        var provider = workspace.Provider;
        var stream = workspace.Stream;
        var ln2 = workspace.Constants.GetLn2(ln2FractionBits);
        var ln2Bits = 32 * ln2.NumWords;

        // |e| < 2^32 adds at most one word.
        var words = ln2.NumWords + 1;
        using var padded = workspace.RentTemp(words);
        provider.ClearMemoryAsync(padded.DevicePointer, words, stream);
        provider.CopyDeviceToDevice(padded.DevicePointer, ln2.DevicePointer, ln2.NumWords, stream);
        using var scaled = workspace.RentTemp(words);
        GpuBigIntMath.Mul(padded, checked((uint)Math.Abs(e)), scaled);
        GpuBigIntMath.ShiftAndResize(scaled, fractionBits - ln2Bits, target);
    }

    // floor((A + 2^(g-1)) / 2^g) = floor((floor(A / 2^(g-1)) + 1) / 2), normalized to the result words.
    private static GpuFloatResult RoundToNearest(
        GpuWorkspace workspace, GpuBigUInt approximation, long approximationExponent, int guardBits, GpuBigUInt result,
        bool negative)
    {
        var words = result.NumWords + 1;
        using var kept = workspace.RentTemp(words);
        GpuBigIntMath.ShiftAndResize(approximation, 1 - guardBits, kept);
        using var rounded = workspace.RentTemp(words);
        GpuBigIntMath.Add(kept, 1u, rounded);
        var shift = GpuBigIntMath.NormalizeToMsb(rounded, result, out _);
        return new GpuFloatResult(checked((int)(approximationExponent + guardBits - 1 - shift)), negative);
    }

    // quotient = floor(dividend / divisor) without reading the remainder back.
    private static void DivideByUInt32Async(
        GpuWorkspace workspace, GpuBigUInt dividend, uint divisor, GpuBigUInt quotient, GpuBigUInt discardedRemainder) =>
        GpuBigIntScalarKernels.LaunchScalarDivByUInt32KernelPtr(
            dividend.DevicePointer, divisor, quotient.DevicePointer, discardedRemainder.DevicePointer,
            dividend.NumWords, gridDimX: 1, blockDimX: 32, hStream: workspace.Stream);

    // A borrowed view of `words` words of `buffer` from word `offset`.
    private static GpuBigUInt Slice(GpuWorkspace workspace, GpuBigUInt buffer, int offset, int words) =>
        new GpuBigIntMemoryHandle(buffer.Handle.Buffer, buffer.Handle.WordOffset + offset, words)
            .AsBorrowedBigUInt(workspace);

    // |y| = Reduced / Denominator with the bit lengths b of the argument and bu of Reduced.
    private readonly struct ReducedRatio(GpuBigUInt reduced, GpuBigUInt denominator, int bitLength, int reducedBits)
    {
        public GpuBigUInt Reduced => reduced;
        public GpuBigUInt Denominator => denominator;
        public int BitLength => bitLength;
        public int ReducedBits => reducedBits;
    }
}
