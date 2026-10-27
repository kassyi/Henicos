namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// GPU implementation of the big-float transcendental functions of <see cref="GSharpKernelFloatProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm and every truncation match the host implementation <see cref="BigFloatTranscendentalMath"/>, which
/// documents the mathematics. The term counts, block sizes and precisions come from <see cref="TranscendentalPlanner"/>
/// before any device work, so the series run without reading values back; the host waits for the device only to read
/// the leading bits of the argument (and, for Log, the bit length of the reduced argument and inside the division), to
/// normalize the approximation and to read its guard words for the rounding check. Pow also reads its two operands
/// once, for the exact check, and the leading bits of <c>y·L</c>.
/// </para>
/// <para>
/// Fixed-point values with <c>W</c> fraction bits occupy <c>W / 32</c> words, plus one integer word where the value
/// may reach 1. The top part of a product is a view of the product buffer, not a copy.
/// </para>
/// </remarks>
internal static class GpuBigFloatTranscendentals
{
    /// <summary>
    /// Computes <c>RZ_p(ln x)</c> for a positive operand on the GPU (see
    /// <see cref="BigFloatTranscendentalMath.Log(BigInteger, int, int, GpuBigFloatTranscendentalOptions)"/>).
    /// </summary>
    /// <param name="workspace">The workspace whose stream orders the work and whose options tune it.</param>
    /// <param name="value">The operand; its sign is ignored (the caller rejects negative values).</param>
    /// <param name="resultSig">The result significand; its word count sets the precision.</param>
    /// <returns>The exponent and sign of the result.</returns>
    /// <exception cref="InvalidOperationException">The operand is zero.</exception>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/>.</exception>
    internal static GpuFloatResult Log(GpuWorkspace workspace, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig) =>
        Log(workspace, value, resultSig, out _);

    /// <summary>
    /// Computes <c>RZ_p(ln x)</c> as <see cref="Log(GpuWorkspace, in GpuFloatOperand, GpuBigIntMemoryHandle)"/> does, and reports the number of attempts of the rounding check.
    /// </summary>
    /// <param name="workspace">The workspace whose stream orders the work and whose options tune it.</param>
    /// <param name="value">The operand; its sign is ignored (the caller rejects negative values).</param>
    /// <param name="resultSig">The result significand; its word count sets the precision.</param>
    /// <param name="attempts">When this method returns, the number of approximations computed (1 unless the rounding
    /// check had to retry).</param>
    /// <returns>The exponent and sign of the result.</returns>
    internal static GpuFloatResult Log(
        GpuWorkspace workspace, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig, out int attempts)
    {
        var result = resultSig.AsBorrowedBigUInt(workspace);
        var (bitLength, leadingBits) = ReadLeadingBits(workspace, value);
        attempts = 0;
        var extraGuardWords = 0;
        while (true)
        {
            attempts++;
            using var approximation = ComputeLog(
                workspace, value, 32 * result.NumWords, bitLength, leadingBits, extraGuardWords);
            if (approximation.IsZero)
            {
                workspace.Provider.ClearMemoryAsync(result.DevicePointer, result.NumWords, workspace.Stream);
                return new GpuFloatResult(0, false);
            }

            if (TryTruncate(workspace, approximation, approximation.ErrorBound, result, out var exponent))
            {
                return new GpuFloatResult(checked((int)exponent), approximation.IsNegative);
            }

            extraGuardWords += TranscendentalPlanner.ZivRetryGuardWords(approximation.WorkingBits);
        }
    }

    /// <summary>
    /// Computes the <c>W</c>-bit approximation of <c>ln x</c> on the GPU and reads it back (see
    /// <see cref="BigFloatTranscendentalMath.LogApproximation"/>).
    /// </summary>
    internal static TranscendentalApproximation LogApproximation(
        GpuWorkspace workspace, in GpuFloatOperand value, int resultWords, int extraGuardWords = 0)
    {
        var (bitLength, leadingBits) = ReadLeadingBits(workspace, value);
        using var approximation = ComputeLog(workspace, value, 32 * resultWords, bitLength, leadingBits, extraGuardWords);
        return approximation.ToHost();
    }

    /// <summary>
    /// Computes <c>RZ_p(e^x)</c> on the GPU (see <see cref="BigFloatTranscendentalMath.Exp(BigInteger, int, bool, int, GpuBigFloatTranscendentalOptions)"/>).
    /// </summary>
    /// <param name="workspace">The workspace whose stream orders the work and whose options tune it.</param>
    /// <param name="value">The operand.</param>
    /// <param name="resultSig">The result significand; its word count sets the precision.</param>
    /// <returns>The exponent and sign (always positive) of the result.</returns>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/> (an underflow,
    /// named in the message, for a negative argument).</exception>
    internal static GpuFloatResult Exp(GpuWorkspace workspace, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig) =>
        Exp(workspace, value, resultSig, out _);

    /// <summary>
    /// Computes <c>RZ_p(e^x)</c> as <see cref="Exp(GpuWorkspace, in GpuFloatOperand, GpuBigIntMemoryHandle)"/> does, and reports the number of attempts of the rounding check.
    /// </summary>
    /// <param name="workspace">The workspace whose stream orders the work and whose options tune it.</param>
    /// <param name="value">The operand.</param>
    /// <param name="resultSig">The result significand; its word count sets the precision.</param>
    /// <param name="attempts">When this method returns, the number of approximations computed (0 for the shortcuts, 1
    /// unless the rounding check had to retry).</param>
    /// <returns>The exponent and sign (always positive) of the result.</returns>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/>.</exception>
    internal static GpuFloatResult Exp(
        GpuWorkspace workspace, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig, out int attempts)
    {
        attempts = 0;
        var result = resultSig.AsBorrowedBigUInt(workspace);
        var resultBits = 32 * result.NumWords;
        var source = value.Significand.AsBorrowedBigUInt(workspace);
        workspace.Provider.ReadLeadingBits(workspace, source, out var bitLength, out var leadingBits);
        if (bitLength == 0)
        {
            return WriteOne(workspace, result);
        }

        // |x| < 2^top. |x| >= 2^32 is out of range for every p (PlanExp rejects it as well); checking it here keeps an
        // argument with a huge exponent from being read as a double (Math.ScaleB to infinity) and planned at all.
        var top = (long)value.Exponent + bitLength;
        if (top > 32)
        {
            throw TranscendentalPlanner.ExpOutOfRange(value.IsNegative);
        }

        // |x| < 2^-(p+1): e^x < 1 + 2^-p for x > 0 and e^x > 1 - 2^-(p+1) > 1 - 2^-p for x < 0, so RZ(e^x) is 1 or
        // 1 - 2^-p without any series.
        if (top <= -(resultBits + 1))
        {
            return value.IsNegative ? WriteLargestBelowOne(workspace, result) : WriteOne(workspace, result);
        }

        var extraGuardWords = 0;
        while (true)
        {
            attempts++;
            using var approximation = ComputeExp(workspace, value, resultBits, bitLength, leadingBits, extraGuardWords);
            if (TryTruncate(workspace, approximation, approximation.ErrorBound, result, out var exponent))
            {
                return new GpuFloatResult(TranscendentalPlanner.ToExpResultExponent(exponent), false);
            }

            extraGuardWords += TranscendentalPlanner.ZivRetryGuardWords(approximation.WorkingBits);
        }
    }

    /// <summary>
    /// Computes the approximation of <c>e^x</c> on the GPU and reads it back, for an argument outside the shortcuts of
    /// <see cref="Exp(GpuWorkspace, in GpuFloatOperand, GpuBigIntMemoryHandle)"/> (see <see cref="BigFloatTranscendentalMath.ExpApproximation"/>).
    /// </summary>
    internal static TranscendentalApproximation ExpApproximation(
        GpuWorkspace workspace, in GpuFloatOperand value, int resultWords, int extraGuardWords = 0)
    {
        var source = value.Significand.AsBorrowedBigUInt(workspace);
        workspace.Provider.ReadLeadingBits(workspace, source, out var bitLength, out var leadingBits);
        using var approximation = ComputeExp(workspace, value, 32 * resultWords, bitLength, leadingBits, extraGuardWords);
        return approximation.ToHost();
    }

    /// <summary>
    /// Computes <c>RZ_p(x^y)</c> for a positive base on the GPU (see
    /// <see cref="BigFloatTranscendentalMath.Pow(BigInteger, int, BigInteger, int, bool, int, GpuBigFloatTranscendentalOptions)"/>).
    /// </summary>
    /// <param name="workspace">The workspace whose stream orders the work and whose options tune it.</param>
    /// <param name="x">The base; its sign is ignored (the caller rejects negative values).</param>
    /// <param name="y">The exponent.</param>
    /// <param name="resultSig">The result significand; its word count sets the precision.</param>
    /// <returns>The exponent and sign (always positive) of the result.</returns>
    /// <exception cref="InvalidOperationException">The base is zero.</exception>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/> (an underflow,
    /// named in the message, for a result below 1).</exception>
    internal static GpuFloatResult Pow(
        GpuWorkspace workspace, in GpuFloatOperand x, in GpuFloatOperand y, GpuBigIntMemoryHandle resultSig) =>
        Pow(workspace, x, y, resultSig, out _);

    /// <summary>
    /// Computes <c>RZ_p(x^y)</c> as <see cref="Pow(GpuWorkspace, in GpuFloatOperand, in GpuFloatOperand, GpuBigIntMemoryHandle)"/>
    /// does, and reports the number of attempts of the rounding check.
    /// </summary>
    /// <param name="workspace">The workspace whose stream orders the work and whose options tune it.</param>
    /// <param name="x">The base; its sign is ignored (the caller rejects negative values).</param>
    /// <param name="y">The exponent.</param>
    /// <param name="resultSig">The result significand; its word count sets the precision.</param>
    /// <param name="attempts">When this method returns, the number of approximations computed (0 for an exact power,
    /// 1 unless the rounding check had to retry).</param>
    /// <returns>The exponent and sign (always positive) of the result.</returns>
    /// <remarks>
    /// The exact check is the host's (<see cref="BigFloatTranscendentalMath.TryExactPow"/>), on the two operands read
    /// back once; the approximations run on the device.
    /// </remarks>
    internal static GpuFloatResult Pow(
        GpuWorkspace workspace, in GpuFloatOperand x, in GpuFloatOperand y, GpuBigIntMemoryHandle resultSig,
        out int attempts)
    {
        attempts = 0;
        var result = resultSig.AsBorrowedBigUInt(workspace);
        var resultBits = 32 * result.NumWords;
        var xSignificand = x.Significand.AsBorrowedBigUInt(workspace).ToBigInteger();
        if (xSignificand.IsZero)
        {
            throw new InvalidOperationException(BigFloatTranscendentalMath.PowOfZeroMessage);
        }

        var ySignificand = y.Significand.AsBorrowedBigUInt(workspace).ToBigInteger();
        if (BigFloatTranscendentalMath.TryExactPow(
                xSignificand, x.Exponent, ySignificand, y.Exponent, y.IsNegative, resultBits, out var exact,
                out var exactExponent))
        {
            WriteSignificand(workspace, result, exact);
            return new GpuFloatResult(exactExponent, false);
        }

        var xBits = (int)xSignificand.GetBitLength();
        var xLeading = BigFloatTranscendentalMath.LeadingBits(xSignificand);
        var extraGuardWords = 0;
        while (true)
        {
            attempts++;
            using var approximation = ComputePow(
                workspace, x, y, ySignificand, resultBits, xBits, xLeading, extraGuardWords, out var argumentBound);
            if (approximation.IsZero)
            {
                return approximation.IsNegative ? WriteLargestBelowOne(workspace, result) : WriteOne(workspace, result);
            }

            if (TryTruncate(workspace, approximation, approximation.ErrorBound + argumentBound, result, out var exponent))
            {
                return new GpuFloatResult(TranscendentalPlanner.ToResultExponent(exponent, "Pow"), false);
            }

            extraGuardWords += TranscendentalPlanner.ZivRetryGuardWords(approximation.WorkingBits);
        }
    }

    /// <summary>
    /// Computes the approximation of <c>x^y</c> on the GPU and reads it back, with the bound of <c>x^y</c> itself (see
    /// <see cref="BigFloatTranscendentalMath.PowApproximation"/>; the same preconditions, a base other than 1, and the
    /// same zero significand for a tiny <c>y·L</c>).
    /// </summary>
    /// <exception cref="ArgumentException">The base is 1.</exception>
    internal static TranscendentalApproximation PowApproximation(
        GpuWorkspace workspace, in GpuFloatOperand x, in GpuFloatOperand y, int resultWords, int extraGuardWords = 0)
    {
        var xSignificand = x.Significand.AsBorrowedBigUInt(workspace).ToBigInteger();
        var ySignificand = y.Significand.AsBorrowedBigUInt(workspace).ToBigInteger();
        using var approximation = ComputePow(
            workspace, x, y, ySignificand, 32 * resultWords, (int)xSignificand.GetBitLength(),
            BigFloatTranscendentalMath.LeadingBits(xSignificand), extraGuardWords, out var argumentBound);
        var host = approximation.ToHost();
        return host with { ErrorBound = host.ErrorBound + argumentBound };
    }

    // The bit length and the leading 64 bits of the argument (one wait); Log reads them once for all attempts.
    private static (int BitLength, ulong LeadingBits) ReadLeadingBits(GpuWorkspace workspace, in GpuFloatOperand value)
    {
        var source = value.Significand.AsBorrowedBigUInt(workspace);
        workspace.Provider.ReadLeadingBits(workspace, source, out var bitLength, out var leadingBits);
        return (bitLength, leadingBits);
    }

    private static DeviceApproximation ComputeLog(
        GpuWorkspace workspace, in GpuFloatOperand value, int resultBits, int bitLength, ulong leadingBits,
        int extraGuardWords)
    {
        var source = value.Significand.AsBorrowedBigUInt(workspace);
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
        if (reducedBits == 0 && e == 0)
        {
            return new DeviceApproximation(workspace.RentTemp(1), 0, false, BigInteger.Zero, 1, isZero: true);
        }

        var closeness = reducedBits == 0
            ? int.MaxValue
            : upper ? bitLength - reducedBits : bitLength - reducedBits - 1;
        var plan = TranscendentalPlanner.PlanLog(resultBits, e, closeness, workspace.Transcendental, extraGuardWords);
        var ratio = new ReducedRatio(reduced, denominator, bitLength, reducedBits);

        var approximation = workspace.RentTemp(plan.WorkingBits / 32);
        try
        {
            var exponent = e == 0
                ? NormalizeSeries(workspace, ratio, plan, approximation)
                : SumWithLn2(workspace, ratio, plan, e, upper, approximation);
            var negative = e == 0 ? upper : e < 0;
            return new DeviceApproximation(approximation, exponent, negative, plan.ErrorBound, plan.WorkingBits, isZero: false);
        }
        catch
        {
            approximation.Dispose();
            throw;
        }
    }

    private static DeviceApproximation ComputeExp(
        GpuWorkspace workspace, in GpuFloatOperand value, int resultBits, int bitLength, ulong leadingBits,
        int extraGuardWords)
    {
        var source = value.Significand.AsBorrowedBigUInt(workspace);
        var top = (long)value.Exponent + bitLength;
        var xApprox = BigFloatTranscendentalMath.ApproximateArgument(leadingBits, top, value.IsNegative);
        var plan = TranscendentalPlanner.PlanExp(resultBits, xApprox, workspace.Transcendental, extraGuardWords);
        var workingWords = plan.WorkingBits / 32;

        using var r = workspace.RentTemp(workingWords);
        ReduceArgument(workspace, source, value.Exponent, value.IsNegative, plan, r);
        using var series = workspace.RentTemp(workingWords + 1);
        ExpSeriesPatersonStockmeyer(workspace, r, plan.Terms, plan.BlockSize, series);

        // Squarings alternate between two products; each square is the top W/32 + 1 words of the previous product.
        using var even = workspace.RentTemp(2 * workingWords + 2);
        using var odd = workspace.RentTemp(2 * workingWords + 2);
        var q = series;
        for (var i = 0; i < plan.Halvings; i++)
        {
            var product = i % 2 == 0 ? even : odd;
            GpuBigIntMath.Mul(q, q, product);
            q = Slice(workspace, product, workingWords, workingWords + 1);
        }

        // e^x = Q · 2^(k - W); Q < 2^(W+3) fits W + 32 bits without truncation.
        var approximation = workspace.RentTemp(workingWords + 1);
        try
        {
            var shift = GpuBigIntMath.NormalizeToMsb(q, approximation, out _);
            var exponent = plan.Reduction - plan.WorkingBits - shift;
            return new DeviceApproximation(approximation, exponent, false, plan.ErrorBound, plan.WorkingBits, isZero: false);
        }
        catch
        {
            approximation.Dispose();
            throw;
        }
    }

    // e^(y·L) for Pow (see BigFloatTranscendentalMath.PowApproximation) with its own bound B_E, and the argument error D
    // in `argumentBound`; a placeholder (IsZero, IsNegative the sign of y·ln x) when |y·L| < 2^-(p+1).
    private static DeviceApproximation ComputePow(
        GpuWorkspace workspace, in GpuFloatOperand x, in GpuFloatOperand y, BigInteger ySignificand, int resultBits,
        int xBits, ulong xLeading, int extraGuardWords, out BigInteger argumentBound)
    {
        var expBits = TranscendentalPlanner.ExpWorkingBits(resultBits, workspace.Transcendental, extraGuardWords);
        using var log = ComputeLog(workspace, x, TranscendentalPlanner.PowLogResultBits(expBits), xBits, xLeading, 0);
        if (log.IsZero)
        {
            // ln 1 = 0 is a 1-word placeholder, not a value to multiply; Pow returns 1^y = 1 before.
            throw new ArgumentException(BigFloatTranscendentalMath.PowApproximationOfOneMessage, nameof(x));
        }

        // y·L exactly, in the product of the two buffers; it has the sign of y·ln x.
        var ySource = y.Significand.AsBorrowedBigUInt(workspace);
        using var argument = workspace.RentTemp(ySource.NumWords + log.Value.NumWords);
        GpuBigIntMath.Mul(ySource, log.Value, argument);
        workspace.Provider.ReadLeadingBits(workspace, argument, out var argumentBits, out var argumentLeading);
        var argumentExponent = (long)y.Exponent + log.Exponent;
        var negative = y.IsNegative != log.IsNegative;
        var top = argumentExponent + argumentBits;
        if (top > 32)
        {
            throw TranscendentalPlanner.OutOfRange("Pow", negative);
        }

        if (top <= -(resultBits + 1))
        {
            argumentBound = BigInteger.Zero;
            return new DeviceApproximation(workspace.RentTemp(1), 0, negative, BigInteger.Zero, expBits, isZero: true);
        }

        // The planner's range check with the Pow message; ComputeExp repeats it with the same argument.
        _ = TranscendentalPlanner.ExpReduction(
            resultBits, BigFloatTranscendentalMath.ApproximateArgument(argumentLeading, top, negative), "Pow");
        var operand = new GpuFloatOperand(argument.Handle, checked((int)argumentExponent), negative);
        var approximation = ComputeExp(workspace, operand, resultBits, argumentBits, argumentLeading, extraGuardWords);
        try
        {
            argumentBound = TranscendentalPlanner.PowArgumentErrorBound(
                ySignificand, y.Exponent, log.ErrorBound, log.Exponent, approximation.WorkingBits);
            return approximation;
        }
        catch
        {
            approximation.Dispose();
            throw;
        }
    }

    // R = floor((x - k·ln 2) · 2^(W - s)) through Lw fraction bits (ln 2 bits rounded up to a word): R is r / 2^s with
    // W fraction bits.
    private static void ReduceArgument(
        GpuWorkspace workspace, GpuBigUInt source, int exponent, bool negative, in ExpPlan plan, GpuBigUInt r)
    {
        var provider = workspace.Provider;
        var stream = workspace.Stream;
        var ln2 = workspace.Constants.GetLn2(plan.Ln2FractionBits);
        var ln2Bits = 32 * ln2.NumWords;

        // |x| < 2^32 and |k| < 2^32 add at most one integer word each; one more for the sum.
        var words = ln2.NumWords + 2;
        using var x = workspace.RentTemp(words);
        GpuBigIntMath.ShiftAndResize(source, checked(exponent + ln2Bits), x);
        using var padded = workspace.RentTemp(words);
        provider.ClearMemoryAsync(padded.DevicePointer, words, stream);
        provider.CopyDeviceToDevice(padded.DevicePointer, ln2.DevicePointer, ln2.NumWords, stream);
        using var kLn2 = workspace.RentTemp(words);
        GpuBigIntMath.Mul(padded, checked((uint)Math.Abs(plan.Reduction)), kLn2);

        using var reduced = workspace.RentTemp(words);
        if (negative)
        {
            GpuBigIntMath.Sub(kLn2, x, reduced);
        }
        else if (plan.Reduction >= 0)
        {
            GpuBigIntMath.Sub(x, kLn2, reduced);
        }
        else
        {
            GpuBigIntMath.Add(x, kLn2, reduced);
        }

        GpuBigIntMath.ShiftAndResize(reduced, plan.WorkingBits - plan.Halvings - ln2Bits, r);
    }

    // Σ_{j<=N} R^j / j! in Paterson–Stockmeyer form with Smith's division order into `sum` (W / 32 + 1 words); see
    // BigFloatTranscendentalMath.ExpSeriesPatersonStockmeyer.
    private static void ExpSeriesPatersonStockmeyer(
        GpuWorkspace workspace, GpuBigUInt r, int terms, int blockSize, GpuBigUInt sum)
    {
        var provider = workspace.Provider;
        var stream = workspace.Stream;
        var workingWords = r.NumWords;
        var words = workingWords + 1;
        // R_m is needed only by a lower block, which exists only for N >= m.
        using var powers = new PowerTable(workspace, r, Math.Min(blockSize, terms));
        using var quotient = workspace.RentTemp(words);
        using var product = workspace.RentTemp(2 * words);
        using var discardedRemainder = workspace.RentTemp(1);
        var high = Slice(workspace, product, workingWords, words);

        // acc lives in `sum`; each step divides into `quotient` and adds the next lower power back into `sum`.
        var top = terms / blockSize * blockSize;
        provider.CopyDeviceToDevice(sum.DevicePointer, powers[terms - top].DevicePointer, words, stream);
        for (var i = terms - top; i >= 1; i--)
        {
            DivideByUInt32Async(workspace, sum, (uint)(top + i), quotient, discardedRemainder);
            GpuBigIntMath.Add(quotient, powers[i - 1], sum);
        }

        for (var first = top - blockSize; first >= 0; first -= blockSize)
        {
            GpuBigIntMath.Mul(sum, powers[blockSize], product);
            for (var i = blockSize; i >= 1; i--)
            {
                DivideByUInt32Async(workspace, i == blockSize ? high : sum, (uint)(first + i), quotient, discardedRemainder);
                GpuBigIntMath.Add(quotient, powers[i - 1], sum);
            }
        }
    }

    // 1 = 2^(p-1) · 2^(1-p).
    private static GpuFloatResult WriteOne(GpuWorkspace workspace, GpuBigUInt result)
    {
        var provider = workspace.Provider;
        provider.ClearMemoryAsync(result.DevicePointer, result.NumWords, workspace.Stream);
        provider.WriteWord(result.DevicePointer, result.NumWords - 1, 0x80000000u, workspace.Stream);
        return new GpuFloatResult(1 - 32 * result.NumWords, false);
    }

    // A host significand of at most result.NumWords words, zero-extended, to the result.
    private static void WriteSignificand(GpuWorkspace workspace, GpuBigUInt result, BigInteger significand)
    {
        var words = ArrayPool<uint>.Shared.Rent(result.NumWords);
        try
        {
            significand.WriteLimbs(words.AsSpan(0, result.NumWords));
            workspace.Provider.CopyHostToDevice(result.DevicePointer, words, result.NumWords, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(words);
        }
    }

    // 1 - 2^-p = (2^p - 1) · 2^-p, RZ(e^x) for -2^-(p+1) < x < 0.
    private static GpuFloatResult WriteLargestBelowOne(GpuWorkspace workspace, GpuBigUInt result)
    {
        var ones = ArrayPool<uint>.Shared.Rent(result.NumWords);
        try
        {
            Array.Fill(ones, uint.MaxValue, 0, result.NumWords);
            workspace.Provider.CopyHostToDevice(result.DevicePointer, ones, result.NumWords, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(ones);
        }

        return new GpuFloatResult(-32 * result.NumWords, false);
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
        var seriesExponent = EvaluateLogSeries(workspace, ratio, plan, series);
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
        var seriesExponent = EvaluateLogSeries(workspace, ratio, plan, series);

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
    private static long EvaluateLogSeries(GpuWorkspace workspace, in ReducedRatio ratio, in LogPlan plan, GpuBigUInt series)
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
        if (plan.BlockSize == 0)
        {
            AtanhSeriesHorner(workspace, z, plan.Terms, t);
        }
        else
        {
            AtanhSeriesPatersonStockmeyer(workspace, z, plan.Terms, plan.BlockSize, t);
        }

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

    // Σ_{k<N} Z^k / (2k+1) in Paterson–Stockmeyer form into `h` (W / 32 + 1 words); see
    // BigFloatTranscendentalMath.AtanhSeriesPatersonStockmeyer.
    private static void AtanhSeriesPatersonStockmeyer(
        GpuWorkspace workspace, GpuBigUInt z, int terms, int blockSize, GpuBigUInt h)
    {
        var workingWords = z.NumWords;
        var words = workingWords + 1;
        // Z_m is needed only by a lower block, which exists only for N - 1 >= m.
        using var powers = new PowerTable(workspace, z, Math.Min(blockSize, terms - 1));
        using var coefficient = workspace.RentTemp(words);
        using var product = workspace.RentTemp(2 * words);
        using var discardedRemainder = workspace.RentTemp(1);
        var high = Slice(workspace, product, workingWords, words);

        // The top block starts from its first term; every lower block from the top part of H · Z_m.
        var top = (terms - 1) / blockSize * blockSize;
        DivideByUInt32Async(workspace, powers[0], (uint)(2 * top + 1), h, discardedRemainder);
        for (var i = 1; i < terms - top; i++)
        {
            DivideByUInt32Async(workspace, powers[i], (uint)(2 * (top + i) + 1), coefficient, discardedRemainder);
            GpuBigIntMath.Add(h, coefficient, h);
        }

        for (var first = top - blockSize; first >= 0; first -= blockSize)
        {
            GpuBigIntMath.Mul(h, powers[blockSize], product);
            for (var i = 0; i < blockSize; i++)
            {
                DivideByUInt32Async(workspace, powers[i], (uint)(2 * (first + i) + 1), coefficient, discardedRemainder);
                GpuBigIntMath.Add(i == 0 ? high : h, coefficient, h);
            }
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

    // The rounding check of Ziv on the device approximation (see BigFloatTranscendentalMath.TryTruncate). The buffer
    // holds A normalized to fill it: W / 32 words for Log, one more low word for Exp and Pow. The guard words are the
    // W / 32 - n words below the top n; only they are read back (one wait), and on success the top n words are copied
    // to the result on the stream. The bound B is the planner's (design §10.1, re-derived in the review of stages
    // 1–4), for Pow plus the argument error D: a B below the true error would make this check accept a wrong
    // truncation silently.
    private static bool TryTruncate(
        GpuWorkspace workspace, in DeviceApproximation approximation, BigInteger errorBound, GpuBigUInt result,
        out long exponent)
    {
        var provider = workspace.Provider;
        var bufferWords = approximation.Value.NumWords;
        var lowWords = bufferWords - approximation.WorkingBits / 32;
        var guardWords = approximation.WorkingBits / 32 - result.NumWords;
        var guardHost = ArrayPool<uint>.Shared.Rent(guardWords);
        BigInteger guard;
        try
        {
            provider.CopyDeviceToHost(
                guardHost, IntPtr.Add(approximation.Value.DevicePointer, 4 * lowWords), guardWords, workspace.Stream);
            guard = BigIntExtensions.ToBigInteger(guardHost.AsSpan(0, guardWords));
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(guardHost);
        }

        var limit = BigInteger.One << (32 * guardWords);
        if (errorBound > guard || guard + errorBound >= limit)
        {
            exponent = 0;
            return false;
        }

        provider.CopyDeviceToDevice(
            result.DevicePointer, IntPtr.Add(approximation.Value.DevicePointer, 4 * (lowWords + guardWords)),
            result.NumWords, workspace.Stream);
        exponent = approximation.Exponent + 32L * (lowWords + guardWords);
        return true;
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

    // The approximation in a device buffer that it owns: ±Value · 2^Exponent within ErrorBound units of its last bit at
    // W bits (the buffer is filled, its top bit set); a 1-word placeholder (IsZero) when there is no approximation: ln 1
    // = 0 exactly, or a Pow argument y·L below 2^-(p+1), IsNegative then being the sign of y·ln x.
    private readonly struct DeviceApproximation(
        [TakesOwnership] GpuBigUInt value, long exponent, bool isNegative, BigInteger errorBound, int workingBits,
        bool isZero) : IDisposable
    {
        public GpuBigUInt Value => value;
        public long Exponent => exponent;
        public bool IsNegative => isNegative;
        public BigInteger ErrorBound => errorBound;
        public int WorkingBits => workingBits;
        public bool IsZero => isZero;

        public TranscendentalApproximation ToHost() =>
            isZero
                ? new TranscendentalApproximation(BigInteger.Zero, 0, isNegative, BigInteger.Zero, workingBits)
                : new TranscendentalApproximation(value.ToBigInteger(), exponent, isNegative, errorBound, workingBits);

        public void Dispose() => value.Dispose();
    }

    // X_0 = 2^W, X_1 = X, X_i = floor(X_(i-1) · X_1 / 2^W) (odd i) or floor(X_(i/2)² / 2^W) (even i), i <= count, each
    // W / 32 + 1 words; X_i for i >= 2 is the top part of its own product buffer.
    private sealed class PowerTable : IDisposable
    {
        private readonly GpuBigUInt[] _owned;
        private readonly GpuBigUInt[] _powers;

        public PowerTable(GpuWorkspace workspace, GpuBigUInt x, int count)
        {
            var provider = workspace.Provider;
            var stream = workspace.Stream;
            var workingWords = x.NumWords;
            var words = workingWords + 1;
            _owned = new GpuBigUInt[count + 1];
            _powers = new GpuBigUInt[count + 1];
            var created = 0;
            try
            {
                // X_0 and X_1 in buffers of their own; X_i (i >= 2) in products of 2 (W / 32 + 1) words.
                for (; created <= count; created++)
                {
                    _owned[created] = workspace.RentTemp(created <= 1 ? words : 2 * words);
                }

                provider.ClearMemoryAsync(_owned[0].DevicePointer, words, stream);
                provider.WriteWord(_owned[0].DevicePointer, workingWords, 1u, stream);
                _powers[0] = _owned[0];
                if (count >= 1)
                {
                    provider.ClearMemoryAsync(_owned[1].DevicePointer, words, stream);
                    provider.CopyDeviceToDevice(_owned[1].DevicePointer, x.DevicePointer, workingWords, stream);
                    _powers[1] = _owned[1];
                }

                for (var i = 2; i <= count; i++)
                {
                    var left = i % 2 == 0 ? _powers[i / 2] : _powers[i - 1];
                    var right = i % 2 == 0 ? _powers[i / 2] : _powers[1];
                    GpuBigIntMath.Mul(left, right, _owned[i]);
                    _powers[i] = Slice(workspace, _owned[i], workingWords, words);
                }
            }
            catch
            {
                for (var i = 0; i < created; i++)
                {
                    _owned[i].Dispose();
                }

                throw;
            }
        }

        public GpuBigUInt this[int index] => _powers[index];

        public void Dispose()
        {
            foreach (var buffer in _owned)
            {
                buffer.Dispose();
            }
        }
    }
}
