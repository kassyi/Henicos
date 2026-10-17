namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Provides a reference host (CPU) execution implementation for arbitrary-precision floating-point arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Implements <see cref="IGpuBigFloatArithmetic"/> using host-side <see cref="System.Numerics.BigInteger"/> mantissa scaling
/// and bit shifts to validate GPU kernel calculations and support execution on non-GPU host environments.
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


    /// <inheritdoc/>
    public GpuFloatResult Normalize(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
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

            var targetBitLength = resultSig.NumWords * 32;
            var currentBitLength = (int)vValue.GetBitLength();
            var shift = targetBitLength - currentBitLength;

            var vNormalized = shift >= 0 ? (vValue << shift) : (vValue >> -shift);

            ToWords(vNormalized, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, resultWords, resultSig.NumWords, context.Stream);

            return new GpuFloatResult(value.Exponent - shift, value.IsNegative);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }

    /// <inheritdoc/>
    public GpuFloatResult Add(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand left,
        in GpuFloatOperand right,
        GpuBigIntMemoryHandle resultSig)
    {
        var leftSig = left.Significand;
        var rightSig = right.Significand;
        var leftLarger = left.Exponent > right.Exponent || (left.Exponent == right.Exponent && leftSig.NumWords >= rightSig.NumWords);
        var expDiff = leftLarger ? (left.Exponent - right.Exponent) : (right.Exponent - left.Exponent);

        var larger = leftLarger ? left : right;
        var smaller = leftLarger ? right : left;
        var largerSig = larger.Significand;
        var smallerSig = smaller.Significand;

        var largerWords = ArrayPool<uint>.Shared.Rent(largerSig.NumWords);
        var smallerWords = ArrayPool<uint>.Shared.Rent(smallerSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(largerWords, largerSig.DevicePointer, largerSig.NumWords, context.Stream);
            context.GpuWorkspace.Provider.CopyDeviceToHost(smallerWords, smallerSig.DevicePointer, smallerSig.NumWords, context.Stream);

            var vLarger = ToBigInteger(largerWords.AsSpan(0, largerSig.NumWords));
            var vSmaller = ToBigInteger(smallerWords.AsSpan(0, smallerSig.NumWords));

            var vLargerShifted = vLarger << expDiff;

            BigInteger vResult;
            bool resultSign;
            if (larger.IsNegative == smaller.IsNegative)
            {
                vResult = vLargerShifted + vSmaller;
                resultSign = larger.IsNegative;
            }
            else
            {
                var diff = vLargerShifted - vSmaller;
                if (diff < 0)
                {
                    vResult = -diff;
                    resultSign = smaller.IsNegative;
                }
                else
                {
                    vResult = diff;
                    resultSign = larger.IsNegative;
                }
            }

            if (vResult.IsZero)
            {
                Array.Clear(resultWords, 0, resultSig.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, resultWords, resultSig.NumWords, context.Stream);
                return new GpuFloatResult(0, false);
            }

            var targetBitLength = resultSig.NumWords * 32;
            var currentBitLength = (int)vResult.GetBitLength();
            var shift = targetBitLength - currentBitLength;

            var vNormalized = shift >= 0 ? (vResult << shift) : (vResult >> -shift);

            ToWords(vNormalized, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, resultWords, resultSig.NumWords, context.Stream);

            return new GpuFloatResult(smaller.Exponent - shift, resultSign);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(largerWords);
            ArrayPool<uint>.Shared.Return(smallerWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
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
            var tempExp = left.Exponent + right.Exponent;
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

            return new GpuFloatResult(tempExp - shift, tempSign);
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

            var shiftBits = resultSig.NumWords * 32 * 2;
            var vQuotient = (vLeft << shiftBits) / vRight;

            var tempExp = left.Exponent - right.Exponent - shiftBits;
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

            return Normalize(ref context, new GpuFloatOperand(tempHandle, tempExp, resultSign), resultSig);
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

            var tempExp = -value.Exponent - shiftBits;

            using var tempResult = context.GpuWorkspace.RentTemp(resultSig.NumWords * 2);
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

            return Normalize(ref context, new GpuFloatOperand(tempHandle, tempExp, value.IsNegative), resultSig);
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

            if (vValue.IsZero)
            {
                // exp(0) = 1
                var tempWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
                try
                {
                    Array.Clear(tempWords, 0, resultSig.NumWords);
                    tempWords[0] = 1;
                    context.GpuWorkspace.Provider.CopyHostToDevice(resultSig.DevicePointer, tempWords, resultSig.NumWords, context.Stream);
                }
                finally
                {
                    ArrayPool<uint>.Shared.Return(tempWords);
                }
                return new GpuFloatResult(0, false);
            }

            var shiftBits = resultSig.NumWords * 32 + 64;

            var xFixed = vValue;
            var currentExp = value.Exponent;
            if (currentExp < -shiftBits)
            {
                xFixed >>= (-shiftBits - currentExp);
            }
            else
            {
                xFixed <<= (currentExp + shiftBits);
            }

            if (value.IsNegative)
            {
                xFixed = -xFixed;
            }

            var sum = BigInteger.One << shiftBits;
            var term = xFixed;
            sum += term;
            var k = 2;
            while (!term.IsZero)
            {
                term = (term * xFixed) >> shiftBits;
                term /= k;
                sum += term;
                k++;
                if (k > 1000)
                {
                    break; // safety fallback
                }
            }

            var resultSign = sum.Sign < 0;
            if (resultSign)
            {
                sum = -sum;
            }

            var tempExpFixed = -shiftBits;
            var sumWords = Math.Max(1, (int)((sum.GetBitLength() + 31) / 32));

            using var tempResult = context.GpuWorkspace.RentTemp(sumWords);
            var tempHandle = tempResult.Handle;
            var tempWordsArr = ArrayPool<uint>.Shared.Rent(tempResult.NumWords);
            try
            {
                ToWords(sum, tempWordsArr.AsSpan(0, tempResult.NumWords), tempResult.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(tempHandle.DevicePointer, tempWordsArr, tempResult.NumWords, context.Stream);
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(tempWordsArr);
            }

            return Normalize(ref context, new GpuFloatOperand(tempHandle, tempExpFixed, resultSign), resultSig);
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

            var shiftBits = resultSig.NumWords * 32 + 64;

            var xFixed = vValue;
            var currentExp = value.Exponent;
            if (currentExp < -shiftBits)
            {
                xFixed >>= (-shiftBits - currentExp);
            }
            else
            {
                xFixed <<= (currentExp + shiftBits);
            }

            var oneFixed = BigInteger.One << shiftBits;

            // y = (x - 1) / (x + 1)
            var num = xFixed - oneFixed;
            var den = xFixed + oneFixed;
            var y = (num << shiftBits) / den;

            var sum = y;
            var term = y;
            var y2 = (y * y) >> shiftBits;
            var k = 3;
            while (!term.IsZero)
            {
                term = (term * y2) >> shiftBits;
                var add = term / k;
                if (add.IsZero)
                {
                    break;
                }

                sum += add;
                k += 2;
                if (k > 1000)
                {
                    break;
                }
            }

            sum <<= 1;

            var resultSign = sum.Sign < 0;
            if (resultSign)
            {
                sum = -sum;
            }

            var tempExpFixed = -shiftBits;
            var sumWords = Math.Max(1, (int)((sum.GetBitLength() + 31) / 32));

            using var tempResult = context.GpuWorkspace.RentTemp(sumWords);
            var tempHandle = tempResult.Handle;
            var tempWordsArr = ArrayPool<uint>.Shared.Rent(tempResult.NumWords);
            try
            {
                ToWords(sum, tempWordsArr.AsSpan(0, tempResult.NumWords), tempResult.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(tempHandle.DevicePointer, tempWordsArr, tempResult.NumWords, context.Stream);
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(tempWordsArr);
            }

            return Normalize(ref context, new GpuFloatOperand(tempHandle, tempExpFixed, resultSign), resultSig);
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

        var tempWords = resultSig.NumWords;

        using var logResult = context.GpuWorkspace.RentTemp(tempWords);
        var logHandle = logResult.Handle;
        var logRes = Log(ref context, in baseValue, logHandle);

        using var mulResult = context.GpuWorkspace.RentTemp(tempWords);
        var mulHandle = mulResult.Handle;
        var mulRes = Multiply(ref context, in exponent, new GpuFloatOperand(logHandle, logRes.Exponent, logRes.IsNegative), mulHandle);

        return Exp(ref context, new GpuFloatOperand(mulHandle, mulRes.Exponent, mulRes.IsNegative), resultSig);
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
