namespace Kassyi.GSharp.Stdlib.Math.BigFloat.Providers;

using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;
using Runtime;

/// <summary>
/// Host (CPU) fallback execution provider for arbitrary-precision floating-point arithmetic.
/// </summary>
public sealed class CSharpBigFloatArithmetic : IGpuBigFloatArithmetic
{
    public static readonly CSharpBigFloatArithmetic Instance = new();

    private CSharpBigFloatArithmetic()
    {
    }

    private static BigInteger ToBigInteger(uint[] words, int count)
    {
        if (count <= 0)
        {
            return BigInteger.Zero;
        }

        var bytes = new byte[count * 4];
        Buffer.BlockCopy(words, 0, bytes, 0, count * 4);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private static void ToWords(BigInteger value, uint[] destination, int maxWords)
    {
        Array.Clear(destination, 0, maxWords);
        if (value.IsZero)
        {
            return;
        }

        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        var bytesToCopy = Math.Min(bytes.Length, maxWords * 4);
        Buffer.BlockCopy(bytes, 0, destination, 0, bytesToCopy);
    }

    public void Normalize(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var valueWords = new uint[valueSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

        var vValue = ToBigInteger(valueWords, valueSig.NumWords);

        if (vValue.IsZero)
        {
            resultExp = 0;
            resultSign = false;
            var zeros = new uint[resultSig.NumWords];
            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                zeros, resultSig.NumWords, context.Stream);
            return;
        }

        var targetBitLength = resultSig.NumWords * 32;
        var currentBitLength = (int)vValue.GetBitLength();
        var shift = targetBitLength - currentBitLength;

        var vNormalized = shift >= 0 ? (vValue << shift) : (vValue >> -shift);

        resultExp = valueExp - shift;
        resultSign = valueSign;

        var resultWords = new uint[resultSig.NumWords];
        ToWords(vNormalized, resultWords, resultSig.NumWords);
        context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
            resultWords, resultSig.NumWords, context.Stream);
    }

    public void Add(ref BigIntExecutionContext context,
        BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
        BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var leftLarger = leftExp > rightExp || (leftExp == rightExp && leftSig.NumWords >= rightSig.NumWords);
        var expDiff = leftLarger ? (leftExp - rightExp) : (rightExp - leftExp);

        var largerSig = leftLarger ? leftSig : rightSig;
        var smallerSig = leftLarger ? rightSig : leftSig;
        var largerSign = leftLarger ? leftSign : rightSign;
        var smallerSign = leftLarger ? rightSign : leftSign;

        resultExp = leftLarger ? leftExp : rightExp;

        var largerWords = new uint[largerSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(largerWords,
            IntPtr.Add(largerSig.Buffer.Pointer, largerSig.WordOffset * 4), largerSig.NumWords, context.Stream);

        var smallerWords = new uint[smallerSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(smallerWords,
            IntPtr.Add(smallerSig.Buffer.Pointer, smallerSig.WordOffset * 4), smallerSig.NumWords, context.Stream);

        var vLarger = ToBigInteger(largerWords, largerSig.NumWords);
        var vSmaller = ToBigInteger(smallerWords, smallerSig.NumWords);

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
            var zeros = new uint[resultSig.NumWords];
            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                zeros, resultSig.NumWords, context.Stream);
            return;
        }

        var targetBitLength = resultSig.NumWords * 32;
        var currentBitLength = (int)vResult.GetBitLength();
        var shift = targetBitLength - currentBitLength;

        var vNormalized = shift >= 0 ? (vResult << shift) : (vResult >> -shift);

        var baseExp = leftLarger ? rightExp : leftExp;
        resultExp = baseExp - shift;

        var resultWords = new uint[resultSig.NumWords];
        ToWords(vNormalized, resultWords, resultSig.NumWords);
        context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
            resultWords, resultSig.NumWords, context.Stream);
    }

    public void Multiply(ref BigIntExecutionContext context,
        BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
        BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var leftWords = new uint[leftSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(leftWords,
            IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, context.Stream);

        var rightWords = new uint[rightSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(rightWords,
            IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, context.Stream);

        var vLeft = ToBigInteger(leftWords, leftSig.NumWords);
        var vRight = ToBigInteger(rightWords, rightSig.NumWords);

        var vProd = vLeft * vRight;
        var tempExp = leftExp + rightExp;
        var tempSign = !vProd.IsZero && (leftSign != rightSign);

        if (vProd.IsZero)
        {
            resultExp = 0;
            resultSign = false;
            var zeros = new uint[resultSig.NumWords];
            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                zeros, resultSig.NumWords, context.Stream);
            return;
        }

        var targetBitLength = resultSig.NumWords * 32;
        var currentBitLength = (int)vProd.GetBitLength();
        var shift = targetBitLength - currentBitLength;

        var vNormalized = shift >= 0 ? (vProd << shift) : (vProd >> -shift);

        resultExp = tempExp - shift;
        resultSign = tempSign;

        var resultWords = new uint[resultSig.NumWords];
        ToWords(vNormalized, resultWords, resultSig.NumWords);
        context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
            resultWords, resultSig.NumWords, context.Stream);
    }

    public void Divide(ref BigIntExecutionContext context,
        BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
        BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var leftWords = new uint[leftSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(leftWords,
            IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, context.Stream);

        var rightWords = new uint[rightSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(rightWords,
            IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, context.Stream);

        var vLeft = ToBigInteger(leftWords, leftSig.NumWords);
        var vRight = ToBigInteger(rightWords, rightSig.NumWords);

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
            new BigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempResult.NumWords);

        var tempWords = new uint[tempResult.NumWords];
        ToWords(vQuotient, tempWords, tempResult.NumWords);
        context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(tempHandle.Buffer.Pointer, tempHandle.WordOffset * 4),
            tempWords, tempResult.NumWords, context.Stream);

        Normalize(ref context, tempHandle, tempExp, resultSign, resultSig, out resultExp, out resultSign);
    }

    public void Reciprocal(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var valueWords = new uint[valueSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

        var vValue = ToBigInteger(valueWords, valueSig.NumWords);

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
            new BigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempResult.NumWords);

        var tempWords = new uint[tempResult.NumWords];
        ToWords(vQuotient, tempWords, tempResult.NumWords);
        context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(tempHandle.Buffer.Pointer, tempHandle.WordOffset * 4),
            tempWords, tempResult.NumWords, context.Stream);

        Normalize(ref context, tempHandle, tempExp, resultSign, resultSig, out resultExp, out resultSign);
    }

    public void Sqrt(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (valueSign)
        {
            throw new InvalidOperationException("Cannot compute square root of a negative number.");
        }

        var valueWords = new uint[valueSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

        var vValue = ToBigInteger(valueWords, valueSig.NumWords);

        if (vValue.IsZero)
        {
            resultExp = 0;
            resultSign = false;
            var zeros = new uint[resultSig.NumWords];
            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                zeros, resultSig.NumWords, context.Stream);
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

        var resultWords = new uint[resultSig.NumWords];
        ToWords(vNormalized, resultWords, resultSig.NumWords);
        context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
            resultWords, resultSig.NumWords, context.Stream);
    }

    public void Rsqrt(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (valueSign)
        {
            throw new InvalidOperationException("Cannot compute inverse square root of a negative number.");
        }

        var tempWords = resultSig.NumWords;
        using var sqrtResult = context.GpuWorkspace.RentTemp(tempWords);
        var sqrtHandle = new BigIntMemoryHandle(sqrtResult.Handle.Buffer, sqrtResult.Handle.WordOffset, tempWords);

        Sqrt(ref context, valueSig, valueExp, valueSign, sqrtHandle, out var sqrtExp, out var sqrtSign);

        Reciprocal(ref context, sqrtHandle, sqrtExp, sqrtSign, resultSig, out resultExp, out resultSign);
    }

    public void Exp(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var valueWords = new uint[valueSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);
        var vValue = ToBigInteger(valueWords, valueSig.NumWords);

        if (vValue.IsZero)
        {
            // exp(0) = 1
            resultExp = 0;
            resultSign = false;
            var tempWords = new uint[resultSig.NumWords];
            tempWords[0] = 1;
            context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                tempWords, resultSig.NumWords, context.Stream);
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

        using var tempResult = context.GpuWorkspace.RentTemp(resultSig.NumWords + 2);
        var tempHandle =
            new BigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempResult.NumWords);
        var tempWordsArr = new uint[tempResult.NumWords];
        ToWords(sum, tempWordsArr, tempResult.NumWords);
        context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(tempHandle.Buffer.Pointer, tempHandle.WordOffset * 4),
            tempWordsArr, tempResult.NumWords, context.Stream);

        Normalize(ref context, tempHandle, tempExpFixed, resultSign, resultSig, out resultExp, out resultSign);
    }

    public void Log(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (valueSign)
        {
            throw new InvalidOperationException("Log of negative number.");
        }

        var valueWords = new uint[valueSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);
        var vValue = ToBigInteger(valueWords, valueSig.NumWords);

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

        using var tempResult = context.GpuWorkspace.RentTemp(resultSig.NumWords + 2);
        var tempHandle =
            new BigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempResult.NumWords);
        var tempWordsArr = new uint[tempResult.NumWords];
        ToWords(sum, tempWordsArr, tempResult.NumWords);
        context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(tempHandle.Buffer.Pointer, tempHandle.WordOffset * 4),
            tempWordsArr, tempResult.NumWords, context.Stream);

        Normalize(ref context, tempHandle, tempExpFixed, resultSign, resultSig, out resultExp, out resultSign);
    }

    public void Pow(ref BigIntExecutionContext context,
        BigIntMemoryHandle baseSig, int baseExp, bool baseSign,
        BigIntMemoryHandle expSig, int exponentExp, bool exponentSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (baseSign)
        {
            throw new InvalidOperationException("Pow with negative base is not supported.");
        }

        var tempWords = resultSig.NumWords;

        using var logResult = context.GpuWorkspace.RentTemp(tempWords);
        var logHandle = new BigIntMemoryHandle(logResult.Handle.Buffer, logResult.Handle.WordOffset, tempWords);
        Log(ref context, baseSig, baseExp, baseSign, logHandle, out var logExp, out var logSign);

        using var mulResult = context.GpuWorkspace.RentTemp(tempWords);
        var mulHandle = new BigIntMemoryHandle(mulResult.Handle.Buffer, mulResult.Handle.WordOffset, tempWords);
        Multiply(ref context, expSig, exponentExp, exponentSign, logHandle, logExp, logSign, mulHandle, out var mulExp,
            out var mulSign);

        Exp(ref context, mulHandle, mulExp, mulSign, resultSig, out resultExp, out resultSign);
    }

    public void DivideByUInt32(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        uint divisor,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign, out uint remainder)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        resultExp = valueExp;
        resultSign = valueSign;

        var valueWords = new uint[valueSig.NumWords];
        context.GpuWorkspace.Provider.CopyDeviceToHost(valueWords,
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

        var vValue = ToBigInteger(valueWords, valueSig.NumWords);
        var vDivisor = new BigInteger(divisor);

        var vQuotient = BigInteger.DivRem(vValue, vDivisor, out var vRemainder);
        remainder = (uint)vRemainder;

        var resultWords = new uint[resultSig.NumWords];
        ToWords(vQuotient, resultWords, resultSig.NumWords);

        context.GpuWorkspace.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
            resultWords, resultSig.NumWords, context.Stream);
    }
}
