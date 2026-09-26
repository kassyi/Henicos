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

    private CSharpBigFloatArithmetic() { }

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
        context.GpuContext.Provider.CopyDeviceToHost(valueWords, IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

        var vValue = ToBigInteger(valueWords, valueSig.NumWords);
        
        if (vValue.IsZero)
        {
            resultExp = 0;
            resultSign = false;
            var zeros = new uint[resultSig.NumWords];
            context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), zeros, resultSig.NumWords, context.Stream);
            return;
        }

        int targetBitLength = resultSig.NumWords * 32;
        int currentBitLength = (int)vValue.GetBitLength();
        int shift = targetBitLength - currentBitLength;
        
        BigInteger vNormalized = shift >= 0 ? (vValue << shift) : (vValue >> -shift);

        resultExp = valueExp - shift;
        resultSign = valueSign;

        var resultWords = new uint[resultSig.NumWords];
        ToWords(vNormalized, resultWords, resultSig.NumWords);
        context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), resultWords, resultSig.NumWords, context.Stream);
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
        context.GpuContext.Provider.CopyDeviceToHost(largerWords, IntPtr.Add(largerSig.Buffer.Pointer, largerSig.WordOffset * 4), largerSig.NumWords, context.Stream);

        var smallerWords = new uint[smallerSig.NumWords];
        context.GpuContext.Provider.CopyDeviceToHost(smallerWords, IntPtr.Add(smallerSig.Buffer.Pointer, smallerSig.WordOffset * 4), smallerSig.NumWords, context.Stream);

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
            context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), zeros, resultSig.NumWords, context.Stream);
            return;
        }

        int targetBitLength = resultSig.NumWords * 32;
        int currentBitLength = (int)vResult.GetBitLength();
        int shift = targetBitLength - currentBitLength;

        BigInteger vNormalized = shift >= 0 ? (vResult << shift) : (vResult >> -shift);
        
        // vResult's baseline exponent is the smaller exponent
        int baseExp = leftLarger ? rightExp : leftExp;
        resultExp = baseExp - shift;

        var resultWords = new uint[resultSig.NumWords];
        ToWords(vNormalized, resultWords, resultSig.NumWords);
        context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), resultWords, resultSig.NumWords, context.Stream);
    }

    public void Multiply(ref BigIntExecutionContext context,
                  BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                  BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                  BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var leftWords = new uint[leftSig.NumWords];
        context.GpuContext.Provider.CopyDeviceToHost(leftWords, IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, context.Stream);

        var rightWords = new uint[rightSig.NumWords];
        context.GpuContext.Provider.CopyDeviceToHost(rightWords, IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, context.Stream);

        var vLeft = ToBigInteger(leftWords, leftSig.NumWords);
        var vRight = ToBigInteger(rightWords, rightSig.NumWords);

        var vProd = vLeft * vRight;
        int tempExp = leftExp + rightExp;
        bool tempSign = !vProd.IsZero && (leftSign != rightSign);

        // Normalize vProd into resultSig
        if (vProd.IsZero)
        {
            resultExp = 0;
            resultSign = false;
            var zeros = new uint[resultSig.NumWords];
            context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), zeros, resultSig.NumWords, context.Stream);
            return;
        }

        int targetBitLength = resultSig.NumWords * 32;
        int currentBitLength = (int)vProd.GetBitLength();
        int shift = targetBitLength - currentBitLength;

        BigInteger vNormalized = shift >= 0 ? (vProd << shift) : (vProd >> -shift);

        resultExp = tempExp - shift;
        resultSign = tempSign;

        var resultWords = new uint[resultSig.NumWords];
        ToWords(vNormalized, resultWords, resultSig.NumWords);
        context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), resultWords, resultSig.NumWords, context.Stream);
    }

    public void Divide(ref BigIntExecutionContext context, 
                BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var leftWords = new uint[leftSig.NumWords];
        context.GpuContext.Provider.CopyDeviceToHost(leftWords, IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, context.Stream);

        var rightWords = new uint[rightSig.NumWords];
        context.GpuContext.Provider.CopyDeviceToHost(rightWords, IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, context.Stream);

        var vLeft = ToBigInteger(leftWords, leftSig.NumWords);
        var vRight = ToBigInteger(rightWords, rightSig.NumWords);

        if (vRight.IsZero)
        {
            throw new DivideByZeroException("GPU BigFloat CPU Fallback: Division by zero.");
        }

        // To preserve precision, shift left by the desired result size before dividing.
        // vLeft is roughly leftSig.NumWords long. We want the quotient to be resultSig.NumWords long.
        int shiftBits = resultSig.NumWords * 32 * 2;
        var vQuotient = (vLeft << shiftBits) / vRight;
        
        int tempExp = leftExp - rightExp - shiftBits;
        resultSign = !vQuotient.IsZero && (leftSign != rightSign);

        using var tempResult = context.GpuContext.RentTemp(resultSig.NumWords * 2);
        var tempHandle = new BigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempResult.NumWords);
        
        var tempWords = new uint[tempResult.NumWords];
        ToWords(vQuotient, tempWords, tempResult.NumWords);
        context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(tempHandle.Buffer.Pointer, tempHandle.WordOffset * 4), tempWords, tempResult.NumWords, context.Stream);
        
        Normalize(ref context, tempHandle, tempExp, resultSign, resultSig, out resultExp, out resultSign);
    }

    public void Reciprocal(ref BigIntExecutionContext context, 
                    BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                    BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var valueWords = new uint[valueSig.NumWords];
        context.GpuContext.Provider.CopyDeviceToHost(valueWords, IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

        var vValue = ToBigInteger(valueWords, valueSig.NumWords);

        if (vValue.IsZero)
        {
            throw new DivideByZeroException("GPU BigFloat CPU Fallback: Reciprocal of zero.");
        }

        // To preserve precision for the reciprocal, we shift 1 left by a large amount.
        // The dividend is 1. We shift it by (valueSig.NumWords + resultSig.NumWords) * 32
        // so that after dividing by vValue (which is valueSig.NumWords long),
        // the result is resultSig.NumWords long.
        int shiftBits = (valueSig.NumWords + resultSig.NumWords) * 32;
        var dividend = BigInteger.One << shiftBits;
        var vQuotient = dividend / vValue;

        int tempExp = -valueExp - shiftBits;
        resultSign = valueSign;

        using var tempResult = context.GpuContext.RentTemp(resultSig.NumWords * 2);
        var tempHandle = new BigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempResult.NumWords);
        
        var tempWords = new uint[tempResult.NumWords];
        ToWords(vQuotient, tempWords, tempResult.NumWords);
        context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(tempHandle.Buffer.Pointer, tempHandle.WordOffset * 4), tempWords, tempResult.NumWords, context.Stream);
        
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
        context.GpuContext.Provider.CopyDeviceToHost(valueWords, IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, context.Stream);

        var vValue = ToBigInteger(valueWords, valueSig.NumWords);

        if (vValue.IsZero)
        {
            resultExp = 0;
            resultSign = false;
            var zeros = new uint[resultSig.NumWords];
            context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), zeros, resultSig.NumWords, context.Stream);
            return;
        }

        int e = valueExp;
        if (e % 2 != 0)
        {
            vValue <<= 1;
            e--;
        }

        int shiftBits = resultSig.NumWords * 32 * 2;
        vValue <<= shiftBits;

        var vSqrt = BigIntegerExtensions.Sqrt(vValue);

        resultExp = e / 2 - (shiftBits / 2);
        resultSign = false;

        int targetBitLength = resultSig.NumWords * 32;
        int currentBitLength = (int)vSqrt.GetBitLength();
        int shift = targetBitLength - currentBitLength;

        BigInteger vNormalized = shift >= 0 ? (vSqrt << shift) : (vSqrt >> -shift);
        resultExp -= shift;

        var resultWords = new uint[resultSig.NumWords];
        ToWords(vNormalized, resultWords, resultSig.NumWords);
        context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), resultWords, resultSig.NumWords, context.Stream);
    }

    public void Rsqrt(ref BigIntExecutionContext context, 
               BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
               BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (valueSign)
        {
            throw new InvalidOperationException("Cannot compute inverse square root of a negative number.");
        }

        int tempWords = resultSig.NumWords;
        using var sqrtResult = context.GpuContext.RentTemp(tempWords);
        var sqrtHandle = new BigIntMemoryHandle(sqrtResult.Handle.Buffer, sqrtResult.Handle.WordOffset, tempWords);
        
        Sqrt(ref context, valueSig, valueExp, valueSign, sqrtHandle, out int sqrtExp, out bool sqrtSign);
        
        Reciprocal(ref context, sqrtHandle, sqrtExp, sqrtSign, resultSig, out resultExp, out resultSign);
    }

    public void Exp(ref BigIntExecutionContext context, 
             BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
             BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Log(ref BigIntExecutionContext context, 
             BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
             BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Pow(ref BigIntExecutionContext context, 
             BigIntMemoryHandle baseSig, int baseExp, bool baseSign,
             BigIntMemoryHandle expSig, int exponentExp, bool exponentSign,
             BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }
}
