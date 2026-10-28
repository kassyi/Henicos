namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Provides a host (CPU) execution implementation for arbitrary-precision floating-point arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Implements <see cref="IGpuBigFloatArithmetic"/> using host-side <see cref="System.Numerics.BigInteger"/> mantissa scaling
/// and bit shifts, as the fallback of <see cref="GSharpKernelFloatProvider"/> on hosts without a GPU.
/// </para>
/// <para>
/// <b>Transcendental functions:</b> <see cref="Exp"/>, <see cref="Log"/> and <see cref="Pow"/> are not a reference for the
/// GPU: they run the GPU's algorithm with the same host-side plan and the same integer truncations, so the two providers
/// return the same bits, the exact value rounded toward zero. Exp reduces <c>x = k·ln 2 + r</c> and evaluates a
/// Paterson–Stockmeyer Taylor polynomial at <c>r / 2^s</c> followed by <c>s</c> squarings; Log reduces
/// <c>x = m·2^e</c> and evaluates an <c>atanh</c> series; Pow returns an exactly representable power directly and
/// computes every other one as <c>e^(y·L)</c>.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Stateless and thread-safe.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class CSharpCpuBigFloatArithmetic : IGpuBigFloatArithmetic
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="CSharpCpuBigFloatArithmetic"/>.
    /// </summary>
    public static readonly CSharpCpuBigFloatArithmetic Instance = new();

    private CSharpCpuBigFloatArithmetic()
    {
    }

    private static BigInteger ToBigInteger(ReadOnlySpan<uint> words) =>
        BigIntExtensions.ToBigInteger(words);

    private static void ToWords(BigInteger value, Span<uint> destination, int maxWords) =>
        value.WriteLimbs(destination.Slice(0, maxWords));

    // Writes a significand that fits the result words (zero-extended) to the result buffer.
    private static void WriteResult(ref GpuBigIntExecutionContext context, BigInteger significand,
        GpuBigIntMemoryHandle resultSig)
    {
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            Array.Clear(resultWords, 0, resultSig.NumWords);
            ToWords(significand, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, resultWords, resultSig.NumWords,
                context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }


    /// <inheritdoc/>
    public GpuFloatResult Normalize(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig) =>
        NormalizeScaled(ref context, value.Significand, value.Exponent, value.IsNegative, resultSig);

    // Normalizes valueSig·2^exponent into resultSig (RZ) and returns the exponent and the sign of the result. The
    // exponent is computed in long and checked into int at the end (#661 §4.1): an intermediate exponent outside int is
    // fine when the result fits, and a result exponent outside int throws OverflowException instead of wrapping around.
    private static GpuFloatResult NormalizeScaled(
        ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle valueSig,
        long exponent,
        bool isNegative,
        GpuBigIntMemoryHandle resultSig) =>
        WriteNormalized(ref context, ReadSignificand(ref context, valueSig), exponent, isNegative, resultSig);

    // Reads a significand from the device.
    private static BigInteger ReadSignificand(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle significand)
    {
        var words = ArrayPool<uint>.Shared.Rent(significand.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(words, significand.DevicePointer, significand.NumWords,
                context.Stream);
            return ToBigInteger(words.AsSpan(0, significand.NumWords));
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(words);
        }
    }

    // Writes RZ_p(value·2^exponent) to resultSig (0 for a zero value) and returns its exponent and sign; the exponent
    // is checked into int at the end (#661 §4.1).
    private static GpuFloatResult WriteNormalized(
        ref GpuBigIntExecutionContext context,
        BigInteger value,
        long exponent,
        bool isNegative,
        GpuBigIntMemoryHandle resultSig)
    {
        if (value.IsZero)
        {
            WriteResult(ref context, BigInteger.Zero, resultSig);
            return new GpuFloatResult(0, false);
        }

        var shift = resultSig.NumWords * 32L - (long)value.GetBitLength();
        var normalized = shift >= 0 ? value << checked((int)shift) : value >> checked((int)-shift);
        WriteResult(ref context, normalized, resultSig);
        return new GpuFloatResult(checked((int)(exponent - shift)), isNegative);
    }

    /// <inheritdoc/>
    public GpuFloatResult Add(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand left,
        in GpuFloatOperand right,
        GpuBigIntMemoryHandle resultSig)
    {
        var leftIsHigh = BigFloatAddAlignment.LeftIsHigh(in left, in right);
        var high = leftIsHigh ? left : right;
        var low = leftIsHigh ? right : left;
        var highValue = ReadSignificand(ref context, high.Significand);
        var lowValue = ReadSignificand(ref context, low.Significand);
        var lowExponent = low.Exponent;
        if (BigFloatAddAlignment.LowIsSticky(in high, in low, resultSig.NumWords))
        {
            // L lies below 2^(e_H - p) (#775): a zero operand leaves the other one, and a nonzero L only matters as
            // the sticky bit 2^(e_H - p - 1), the same as on the GPU.
            if (lowValue.IsZero)
            {
                return WriteNormalized(ref context, highValue, high.Exponent, high.IsNegative, resultSig);
            }

            if (highValue.IsZero)
            {
                return WriteNormalized(ref context, lowValue, low.Exponent, low.IsNegative, resultSig);
            }

            lowValue = BigInteger.One;
            lowExponent = BigFloatAddAlignment.StickyExponent(in high, resultSig.NumWords);
        }

        // The exact sum at the exponent of L, with H shifted onto it.
        var highShifted = highValue << BigFloatAddAlignment.ShiftBits(high.Exponent, lowExponent);
        if (high.IsNegative == low.IsNegative)
        {
            return WriteNormalized(ref context, highShifted + lowValue, lowExponent, high.IsNegative, resultSig);
        }

        var difference = highShifted - lowValue;
        var isNegative = difference.Sign < 0 ? low.IsNegative : high.IsNegative;
        return WriteNormalized(ref context, BigInteger.Abs(difference), lowExponent, isNegative, resultSig);
    }

    /// <inheritdoc/>
    public GpuFloatResult Multiply(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand left,
        in GpuFloatOperand right,
        GpuBigIntMemoryHandle resultSig)
    {
        var leftSig = left.Significand;
        var rightSig = right.Significand;
        var leftWords = ArrayPool<uint>.Shared.Rent(leftSig.NumWords);
        var rightWords = ArrayPool<uint>.Shared.Rent(rightSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(leftWords, leftSig.DevicePointer, leftSig.NumWords, context.Stream);
            context.GpuWorkspace.Provider.CopyDeviceToHost(rightWords, rightSig.DevicePointer, rightSig.NumWords, context.Stream);

            var vLeft = ToBigInteger(leftWords.AsSpan(0, leftSig.NumWords));
            var vRight = ToBigInteger(rightWords.AsSpan(0, rightSig.NumWords));

            var vProd = vLeft * vRight;
            var tempExp = (long)left.Exponent + right.Exponent;
            var tempSign = !vProd.IsZero && (left.IsNegative != right.IsNegative);

            if (vProd.IsZero)
            {
                Array.Clear(resultWords, 0, resultSig.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, resultWords, resultSig.NumWords, context.Stream);
                return new GpuFloatResult(0, false);
            }

            var targetBitLength = resultSig.NumWords * 32;
            var currentBitLength = (int)vProd.GetBitLength();
            var shift = targetBitLength - currentBitLength;

            var vNormalized = shift >= 0 ? (vProd << shift) : (vProd >> -shift);

            ToWords(vNormalized, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, resultWords, resultSig.NumWords, context.Stream);

            // eL + eR is computed in long; the exponent of the result must fit in int (#661 §4.1).
            return new GpuFloatResult(checked((int)(tempExp - shift)), tempSign);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(leftWords);
            ArrayPool<uint>.Shared.Return(rightWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }

    /// <inheritdoc/>
    public GpuFloatResult Divide(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand left,
        in GpuFloatOperand right,
        GpuBigIntMemoryHandle resultSig)
    {
        var leftSig = left.Significand;
        var rightSig = right.Significand;
        var leftWords = ArrayPool<uint>.Shared.Rent(leftSig.NumWords);
        var rightWords = ArrayPool<uint>.Shared.Rent(rightSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(leftWords, leftSig.DevicePointer, leftSig.NumWords, context.Stream);
            context.GpuWorkspace.Provider.CopyDeviceToHost(rightWords, rightSig.DevicePointer, rightSig.NumWords, context.Stream);

            var vLeft = ToBigInteger(leftWords.AsSpan(0, leftSig.NumWords));
            var vRight = ToBigInteger(rightWords.AsSpan(0, rightSig.NumWords));

            if (vRight.IsZero)
            {
                throw new DivideByZeroException("GPU BigFloat CPU Fallback: Division by zero.");
            }

            // floor(L·2^s / R) has at least p + 1 bits for s = 32·(n + Rw), whatever the widths of L and R (#661).
            var shiftBits = (resultSig.NumWords + rightSig.NumWords) * 32;
            var vQuotient = (vLeft << shiftBits) / vRight;

            var tempExp = (long)left.Exponent - right.Exponent - shiftBits;
            var resultSign = !vQuotient.IsZero && (left.IsNegative != right.IsNegative);

            var quotientWords = (int)((vQuotient.GetBitLength() + 31) / 32);
            if (quotientWords == 0)
            {
                quotientWords = 1;
            }

            using var tempResult = context.GpuWorkspace.RentTemp(quotientWords);
            var tempHandle = tempResult.Handle;

            var tempWords = ArrayPool<uint>.Shared.Rent(tempResult.NumWords);
            try
            {
                ToWords(vQuotient, tempWords.AsSpan(0, tempResult.NumWords), tempResult.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(tempHandle.DevicePointer, tempWords, tempResult.NumWords, context.Stream);
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(tempWords);
            }

            return NormalizeScaled(ref context, tempHandle, tempExp, resultSign, resultSig);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(leftWords);
            ArrayPool<uint>.Shared.Return(rightWords);
        }
    }

    /// <inheritdoc/>
    public GpuFloatResult Reciprocal(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
        var valueSig = value.Significand;
        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords, valueSig.DevicePointer, valueSig.NumWords, context.Stream);

            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));

            if (vValue.IsZero)
            {
                throw new DivideByZeroException("GPU BigFloat CPU Fallback: Reciprocal of zero.");
            }

            var shiftBits = (valueSig.NumWords + resultSig.NumWords) * 32;
            var dividend = BigInteger.One << shiftBits;
            var vQuotient = dividend / vValue;

            var tempExp = -(long)value.Exponent - shiftBits;

            // The quotient has up to 32·(v + n) - bits(V) + 2 bits: more than 2n words when V has leading zero words, so
            // the buffer is sized from the quotient (as in Divide); a shorter one would drop its top words (#661).
            var quotientWords = (int)((vQuotient.GetBitLength() + 31) / 32);
            using var tempResult = context.GpuWorkspace.RentTemp(quotientWords);
            var tempHandle = tempResult.Handle;

            var tempWords = ArrayPool<uint>.Shared.Rent(tempResult.NumWords);
            try
            {
                ToWords(vQuotient, tempWords.AsSpan(0, tempResult.NumWords), tempResult.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(tempHandle.DevicePointer, tempWords, tempResult.NumWords, context.Stream);
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(tempWords);
            }

            return NormalizeScaled(ref context, tempHandle, tempExp, value.IsNegative, resultSig);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
        }
    }

    /// <inheritdoc/>
    public GpuFloatResult Sqrt(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
        if (value.IsNegative)
        {
            throw new InvalidOperationException("Cannot compute square root of a negative number.");
        }

        var valueSig = value.Significand;
        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords, valueSig.DevicePointer, valueSig.NumWords, context.Stream);

            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));

            if (vValue.IsZero)
            {
                Array.Clear(resultWords, 0, resultSig.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, resultWords, resultSig.NumWords, context.Stream);
                return new GpuFloatResult(0, false);
            }

            var e = value.Exponent;
            if (e % 2 != 0)
            {
                vValue <<= 1;
                e--;
            }

            var shiftBits = resultSig.NumWords * 32 * 2;
            vValue <<= shiftBits;

            var vSqrt = vValue.Sqrt();

            var resultExp = e / 2 - (shiftBits / 2);

            var targetBitLength = resultSig.NumWords * 32;
            var currentBitLength = (int)vSqrt.GetBitLength();
            var shift = targetBitLength - currentBitLength;

            var vNormalized = shift >= 0 ? (vSqrt << shift) : (vSqrt >> -shift);
            resultExp -= shift;

            ToWords(vNormalized, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, resultWords, resultSig.NumWords, context.Stream);

            return new GpuFloatResult(resultExp, false);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }

    /// <inheritdoc/>
    public GpuFloatResult Rsqrt(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
        if (value.IsNegative)
        {
            throw new InvalidOperationException("Cannot compute inverse square root of a negative number.");
        }

        var valueSig = value.Significand;
        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords, valueSig.DevicePointer, valueSig.NumWords, context.Stream);

            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));
            if (vValue.IsZero)
            {
                throw new DivideByZeroException("GPU BigFloat CPU Fallback: Inverse square root of zero.");
            }

            var e = value.Exponent;
            if (e % 2 != 0)
            {
                vValue <<= 1;
                e--;
            }

            var shiftBits = (valueSig.NumWords + resultSig.NumWords * 2) * 32;
            var ySquared = (BigInteger.One << (2 * shiftBits)) / vValue;
            var vRsqrt = ySquared.Sqrt();

            var resultExp = -(e / 2) - shiftBits;

            var targetBitLength = resultSig.NumWords * 32;
            var currentBitLength = (int)vRsqrt.GetBitLength();
            var shift = targetBitLength - currentBitLength;

            var vNormalized = shift >= 0 ? (vRsqrt << shift) : (vRsqrt >> -shift);
            resultExp -= shift;

            ToWords(vNormalized, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, resultWords, resultSig.NumWords, context.Stream);

            return new GpuFloatResult(resultExp, false);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }

    /// <inheritdoc/>
    public GpuFloatResult Exp(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
        var valueSig = value.Significand;
        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords, valueSig.DevicePointer, valueSig.NumWords, context.Stream);
            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));

            var (significand, exponent) = BigFloatTranscendentalMath.Exp(
                vValue, value.Exponent, value.IsNegative, resultSig.NumWords * 32, context.GpuWorkspace.Transcendental);
            WriteResult(ref context, significand, resultSig);
            return new GpuFloatResult(exponent, false);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
        }
    }

    /// <inheritdoc/>
    public GpuFloatResult Log(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
        if (value.IsNegative)
        {
            throw new InvalidOperationException("Log of negative number.");
        }

        var valueSig = value.Significand;
        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords, valueSig.DevicePointer, valueSig.NumWords, context.Stream);
            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));

            if (vValue.IsZero)
            {
                throw new InvalidOperationException("Log of zero.");
            }

            var (significand, exponent, isNegative) = BigFloatTranscendentalMath.Log(
                vValue, value.Exponent, resultSig.NumWords * 32, context.GpuWorkspace.Transcendental);
            WriteResult(ref context, significand, resultSig);
            return new GpuFloatResult(exponent, isNegative);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
        }
    }

    /// <inheritdoc/>
    public GpuFloatResult Pow(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand baseValue,
        in GpuFloatOperand exponent,
        GpuBigIntMemoryHandle resultSig)
    {
        if (baseValue.IsNegative)
        {
            throw new InvalidOperationException("Pow with negative base is not supported.");
        }

        var baseSig = baseValue.Significand;
        var exponentSig = exponent.Significand;
        var baseWords = ArrayPool<uint>.Shared.Rent(baseSig.NumWords);
        var exponentWords = ArrayPool<uint>.Shared.Rent(exponentSig.NumWords);
        try
        {
            var provider = context.GpuWorkspace.Provider;
            provider.CopyDeviceToHost(baseWords, baseSig.DevicePointer, baseSig.NumWords, context.Stream);
            provider.CopyDeviceToHost(exponentWords, exponentSig.DevicePointer, exponentSig.NumWords, context.Stream);
            var vBase = ToBigInteger(baseWords.AsSpan(0, baseSig.NumWords));
            var vExponent = ToBigInteger(exponentWords.AsSpan(0, exponentSig.NumWords));

            var (significand, resultExponent) = BigFloatTranscendentalMath.Pow(
                vBase, baseValue.Exponent, vExponent, exponent.Exponent, exponent.IsNegative, resultSig.NumWords * 32,
                context.GpuWorkspace.Transcendental);
            WriteResult(ref context, significand, resultSig);
            return new GpuFloatResult(resultExponent, false);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(baseWords);
            ArrayPool<uint>.Shared.Return(exponentWords);
        }
    }

    /// <inheritdoc/>
    public GpuFloatScalarDivResult DivideByUInt32(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        uint divisor,
        GpuBigIntMemoryHandle resultSig)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        var valueSig = value.Significand;
        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords, valueSig.DevicePointer, valueSig.NumWords, context.Stream);

            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));
            var vDivisor = new BigInteger(divisor);

            var vQuotient = BigInteger.DivRem(vValue, vDivisor, out var vRemainder);
            var remainder = (uint)vRemainder;

            ToWords(vQuotient, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);

            context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, resultWords, resultSig.NumWords, context.Stream);

            return new GpuFloatScalarDivResult(value.Exponent, value.IsNegative, remainder);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }
}
