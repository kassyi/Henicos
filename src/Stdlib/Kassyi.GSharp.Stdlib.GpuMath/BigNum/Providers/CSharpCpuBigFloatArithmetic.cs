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
    public void Normalize(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
                IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));

            if (vValue.IsZero)
            {
                resultExp = 0;
                resultSign = false;
                Array.Clear(resultWords, 0, resultSig.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                    resultWords, resultSig.NumWords, context.Stream);
                return;
            }

            var targetBitLength = resultSig.NumWords * 32;
            var currentBitLength = (int)vValue.GetBitLength();
            var shift = targetBitLength - currentBitLength;

            var vNormalized = shift >= 0 ? (vValue << shift) : (vValue >> -shift);

            resultExp = valueExp - shift;
            resultSign = valueSign;

            ToWords(vNormalized, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                resultWords, resultSig.NumWords, context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }

    /// <inheritdoc/>
    public void Add(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
        GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var leftLarger = leftExp > rightExp || (leftExp == rightExp && leftSig.NumWords >= rightSig.NumWords);
        var expDiff = leftLarger ? (leftExp - rightExp) : (rightExp - leftExp);

        var largerSig = leftLarger ? leftSig : rightSig;
        var smallerSig = leftLarger ? rightSig : leftSig;
        var largerSign = leftLarger ? leftSign : rightSign;
        var smallerSign = leftLarger ? rightSign : leftSign;

        resultExp = leftLarger ? leftExp : rightExp;

        var largerWords = ArrayPool<uint>.Shared.Rent(largerSig.NumWords);
        var smallerWords = ArrayPool<uint>.Shared.Rent(smallerSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(largerWords,
                IntPtr.Add(largerSig.Buffer.Pointer, largerSig.WordOffset * 4), largerSig.NumWords, context.Stream);

            context.GpuWorkspace.Provider.CopyDeviceToHost(smallerWords,
                IntPtr.Add(smallerSig.Buffer.Pointer, smallerSig.WordOffset * 4), smallerSig.NumWords, context.Stream);

            var vLarger = ToBigInteger(largerWords.AsSpan(0, largerSig.NumWords));
            var vSmaller = ToBigInteger(smallerWords.AsSpan(0, smallerSig.NumWords));

            var vLargerShifted = vLarger << expDiff;

            BigInteger vResult;
            if (largerSign == smallerSign)
            {
                vResult = vLargerShifted + vSmaller;
                resultSign = largerSign;
            }
            else
            {
                var diff = vLargerShifted - vSmaller;
                if (diff < 0)
                {
                    vResult = -diff;
                    resultSign = smallerSign;
                }
                else
                {
                    vResult = diff;
                    resultSign = largerSign;
                }
            }

            if (vResult.IsZero)
            {
                resultSign = false;
                resultExp = 0;
                Array.Clear(resultWords, 0, resultSig.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                    resultWords, resultSig.NumWords, context.Stream);
                return;
            }

            var targetBitLength = resultSig.NumWords * 32;
            var currentBitLength = (int)vResult.GetBitLength();
            var shift = targetBitLength - currentBitLength;

            var vNormalized = shift >= 0 ? (vResult << shift) : (vResult >> -shift);

            var baseExp = leftLarger ? rightExp : leftExp;
            resultExp = baseExp - shift;

            ToWords(vNormalized, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                resultWords, resultSig.NumWords, context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(largerWords);
            ArrayPool<uint>.Shared.Return(smallerWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }

    /// <inheritdoc/>
    public void Multiply(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
        GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var leftWords = ArrayPool<uint>.Shared.Rent(leftSig.NumWords);
        var rightWords = ArrayPool<uint>.Shared.Rent(rightSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(leftWords,
                IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, context.Stream);

            context.GpuWorkspace.Provider.CopyDeviceToHost(rightWords,
                IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, context.Stream);

            var vLeft = ToBigInteger(leftWords.AsSpan(0, leftSig.NumWords));
            var vRight = ToBigInteger(rightWords.AsSpan(0, rightSig.NumWords));

            var vProd = vLeft * vRight;
            var tempExp = leftExp + rightExp;
            var tempSign = !vProd.IsZero && (leftSign != rightSign);

            if (vProd.IsZero)
            {
                resultExp = 0;
                resultSign = false;
                Array.Clear(resultWords, 0, resultSig.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                    resultWords, resultSig.NumWords, context.Stream);
                return;
            }

            var targetBitLength = resultSig.NumWords * 32;
            var currentBitLength = (int)vProd.GetBitLength();
            var shift = targetBitLength - currentBitLength;

            var vNormalized = shift >= 0 ? (vProd << shift) : (vProd >> -shift);

            resultExp = tempExp - shift;
            resultSign = tempSign;

            ToWords(vNormalized, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                resultWords, resultSig.NumWords, context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(leftWords);
            ArrayPool<uint>.Shared.Return(rightWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }

    /// <inheritdoc/>
    public void Divide(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
        GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var leftWords = ArrayPool<uint>.Shared.Rent(leftSig.NumWords);
        var rightWords = ArrayPool<uint>.Shared.Rent(rightSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(leftWords,
                IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, context.Stream);

            context.GpuWorkspace.Provider.CopyDeviceToHost(rightWords,
                IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, context.Stream);

            var vLeft = ToBigInteger(leftWords.AsSpan(0, leftSig.NumWords));
            var vRight = ToBigInteger(rightWords.AsSpan(0, rightSig.NumWords));

            if (vRight.IsZero)
            {
                throw new DivideByZeroException("GPU BigFloat CPU Fallback: Division by zero.");
            }

            var shiftBits = resultSig.NumWords * 32 * 2;
            var vQuotient = (vLeft << shiftBits) / vRight;

            var tempExp = leftExp - rightExp - shiftBits;
            resultSign = !vQuotient.IsZero && (leftSign != rightSign);

            var quotientWords = (int)((vQuotient.GetBitLength() + 31) / 32);
            if (quotientWords == 0)
            {
                quotientWords = 1;
            }

            using var tempResult = context.GpuWorkspace.RentTemp(quotientWords);
            var tempHandle =
                new GpuBigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempResult.NumWords);

            var tempWords = ArrayPool<uint>.Shared.Rent(tempResult.NumWords);
            try
            {
                ToWords(vQuotient, tempWords.AsSpan(0, tempResult.NumWords), tempResult.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(tempHandle.Buffer.Pointer, tempHandle.WordOffset * 4),
                    tempWords, tempResult.NumWords, context.Stream);
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(tempWords);
            }

            Normalize(ref context, tempHandle, tempExp, resultSign, resultSig, out resultExp, out resultSign);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(leftWords);
            ArrayPool<uint>.Shared.Return(rightWords);
        }
    }

    /// <inheritdoc/>
    public void Reciprocal(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
                IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));

            if (vValue.IsZero)
            {
                throw new DivideByZeroException("GPU BigFloat CPU Fallback: Reciprocal of zero.");
            }

            var shiftBits = (valueSig.NumWords + resultSig.NumWords) * 32;
            var dividend = BigInteger.One << shiftBits;
            var vQuotient = dividend / vValue;

            var tempExp = -valueExp - shiftBits;
            resultSign = valueSign;

            using var tempResult = context.GpuWorkspace.RentTemp(resultSig.NumWords * 2);
            var tempHandle =
                new GpuBigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempResult.NumWords);

            var tempWords = ArrayPool<uint>.Shared.Rent(tempResult.NumWords);
            try
            {
                ToWords(vQuotient, tempWords.AsSpan(0, tempResult.NumWords), tempResult.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(tempHandle.Buffer.Pointer, tempHandle.WordOffset * 4),
                    tempWords, tempResult.NumWords, context.Stream);
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(tempWords);
            }

            Normalize(ref context, tempHandle, tempExp, resultSign, resultSig, out resultExp, out resultSign);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
        }
    }

    /// <inheritdoc/>
    public void Sqrt(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (valueSign)
        {
            throw new InvalidOperationException("Cannot compute square root of a negative number.");
        }

        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
                IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));

            if (vValue.IsZero)
            {
                resultExp = 0;
                resultSign = false;
                Array.Clear(resultWords, 0, resultSig.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                    resultWords, resultSig.NumWords, context.Stream);
                return;
            }

            var e = valueExp;
            if (e % 2 != 0)
            {
                vValue <<= 1;
                e--;
            }

            var shiftBits = resultSig.NumWords * 32 * 2;
            vValue <<= shiftBits;

            var vSqrt = vValue.Sqrt();

            resultExp = e / 2 - (shiftBits / 2);
            resultSign = false;

            var targetBitLength = resultSig.NumWords * 32;
            var currentBitLength = (int)vSqrt.GetBitLength();
            var shift = targetBitLength - currentBitLength;

            var vNormalized = shift >= 0 ? (vSqrt << shift) : (vSqrt >> -shift);
            resultExp -= shift;

            ToWords(vNormalized, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                resultWords, resultSig.NumWords, context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }

    /// <inheritdoc/>
    public void Rsqrt(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (valueSign)
        {
            throw new InvalidOperationException("Cannot compute inverse square root of a negative number.");
        }

        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
                IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));
            if (vValue.IsZero)
            {
                throw new DivideByZeroException("GPU BigFloat CPU Fallback: Inverse square root of zero.");
            }

            var e = valueExp;
            if (e % 2 != 0)
            {
                vValue <<= 1;
                e--;
            }

            var shiftBits = (valueSig.NumWords + resultSig.NumWords * 2) * 32;
            var ySquared = (BigInteger.One << (2 * shiftBits)) / vValue;
            var vRsqrt = ySquared.Sqrt();

            resultExp = -(e / 2) - shiftBits;
            resultSign = false;

            var targetBitLength = resultSig.NumWords * 32;
            var currentBitLength = (int)vRsqrt.GetBitLength();
            var shift = targetBitLength - currentBitLength;

            var vNormalized = shift >= 0 ? (vRsqrt << shift) : (vRsqrt >> -shift);
            resultExp -= shift;

            ToWords(vNormalized, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);
            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                resultWords, resultSig.NumWords, context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }

    /// <inheritdoc/>
    public void Exp(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
                IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);
            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));

            if (vValue.IsZero)
            {
                // exp(0) = 1
                resultExp = 0;
                resultSign = false;
                var tempWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
                try
                {
                    Array.Clear(tempWords, 0, resultSig.NumWords);
                    tempWords[0] = 1;
                    context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                        tempWords, resultSig.NumWords, context.Stream);
                }
                finally
                {
                    ArrayPool<uint>.Shared.Return(tempWords);
                }
                return;
            }

            var shiftBits = resultSig.NumWords * 32 + 64;

            var xFixed = vValue;
            var currentExp = valueExp;
            if (currentExp < -shiftBits)
            {
                xFixed >>= (-shiftBits - currentExp);
            }
            else
            {
                xFixed <<= (currentExp + shiftBits);
            }

            if (valueSign)
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

            resultSign = sum.Sign < 0;
            if (resultSign)
            {
                sum = -sum;
            }

            var tempExpFixed = -shiftBits;
            var sumWords = Math.Max(1, (int)((sum.GetBitLength() + 31) / 32));

            using var tempResult = context.GpuWorkspace.RentTemp(sumWords);
            var tempHandle =
                new GpuBigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempResult.NumWords);
            var tempWordsArr = ArrayPool<uint>.Shared.Rent(tempResult.NumWords);
            try
            {
                ToWords(sum, tempWordsArr.AsSpan(0, tempResult.NumWords), tempResult.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(tempHandle.Buffer.Pointer, tempHandle.WordOffset * 4),
                    tempWordsArr, tempResult.NumWords, context.Stream);
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(tempWordsArr);
            }

            Normalize(ref context, tempHandle, tempExpFixed, resultSign, resultSig, out resultExp, out resultSign);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
        }
    }

    /// <inheritdoc/>
    public void Log(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (valueSign)
        {
            throw new InvalidOperationException("Log of negative number.");
        }

        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
                IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);
            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));

            if (vValue.IsZero)
            {
                throw new InvalidOperationException("Log of zero.");
            }

            var shiftBits = resultSig.NumWords * 32 + 64;

            var xFixed = vValue;
            var currentExp = valueExp;
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

            resultSign = sum.Sign < 0;
            if (resultSign)
            {
                sum = -sum;
            }

            var tempExpFixed = -shiftBits;
            var sumWords = Math.Max(1, (int)((sum.GetBitLength() + 31) / 32));

            using var tempResult = context.GpuWorkspace.RentTemp(sumWords);
            var tempHandle =
                new GpuBigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempResult.NumWords);
            var tempWordsArr = ArrayPool<uint>.Shared.Rent(tempResult.NumWords);
            try
            {
                ToWords(sum, tempWordsArr.AsSpan(0, tempResult.NumWords), tempResult.NumWords);
                context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(tempHandle.Buffer.Pointer, tempHandle.WordOffset * 4),
                    tempWordsArr, tempResult.NumWords, context.Stream);
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(tempWordsArr);
            }

            Normalize(ref context, tempHandle, tempExpFixed, resultSign, resultSig, out resultExp, out resultSign);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
        }
    }

    /// <inheritdoc/>
    public void Pow(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle baseSig, int baseExp, bool baseSign,
        GpuBigIntMemoryHandle expSig, int exponentExp, bool exponentSign,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (baseSign)
        {
            throw new InvalidOperationException("Pow with negative base is not supported.");
        }

        var tempWords = resultSig.NumWords;

        using var logResult = context.GpuWorkspace.RentTemp(tempWords);
        var logHandle = new GpuBigIntMemoryHandle(logResult.Handle.Buffer, logResult.Handle.WordOffset, tempWords);
        Log(ref context, baseSig, baseExp, baseSign, logHandle, out var logExp, out var logSign);

        using var mulResult = context.GpuWorkspace.RentTemp(tempWords);
        var mulHandle = new GpuBigIntMemoryHandle(mulResult.Handle.Buffer, mulResult.Handle.WordOffset, tempWords);
        Multiply(ref context, expSig, exponentExp, exponentSign, logHandle, logExp, logSign, mulHandle, out var mulExp,
            out var mulSign);

        Exp(ref context, mulHandle, mulExp, mulSign, resultSig, out resultExp, out resultSign);
    }

    /// <inheritdoc/>
    public void DivideByUInt32(ref GpuBigIntExecutionContext context,
        GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        uint divisor,
        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign, out uint remainder)

    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        resultExp = valueExp;
        resultSign = valueSign;

        var valueWords = ArrayPool<uint>.Shared.Rent(valueSig.NumWords);
        var resultWords = ArrayPool<uint>.Shared.Rent(resultSig.NumWords);
        try
        {
            context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
                IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

            var vValue = ToBigInteger(valueWords.AsSpan(0, valueSig.NumWords));
            var vDivisor = new BigInteger(divisor);

            var vQuotient = BigInteger.DivRem(vValue, vDivisor, out var vRemainder);
            remainder = (uint)vRemainder;

            ToWords(vQuotient, resultWords.AsSpan(0, resultSig.NumWords), resultSig.NumWords);

            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                resultWords, resultSig.NumWords, context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(valueWords);
            ArrayPool<uint>.Shared.Return(resultWords);
        }
    }
}
