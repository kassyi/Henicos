using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Kernels;

namespace Kassyi.GSharp.Stdlib.Math.BigFloat.Providers;

public sealed class GSharpAotFloatProvider : IGpuBigFloatArithmetic
{
    public static readonly GSharpAotFloatProvider Instance = new();

    private GSharpAotFloatProvider() { }

    public void Normalize(ref BigIntExecutionContext context, 
                   BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                   BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var stream = context.Stream;
        int maxWords = resultSig.NumWords;

        // Step 1: Copy to host to determine shift amount (Phase 1 Hack for TDD)
        // Ideally this should be computed on GPU with CountLeadingZerosKernel.
        var valueWords = new uint[valueSig.NumWords];
        context.GpuContext.Provider.CopyDeviceToHost(valueWords, IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, stream);

        int currentBitLength = 0;
        for (int i = valueWords.Length - 1; i >= 0; i--)
        {
            if (valueWords[i] != 0)
            {
                uint w = valueWords[i];
                int msb = 0;
                while (w > 0)
                {
                    msb++;
                    w >>= 1;
                }
                currentBitLength = i * 32 + msb;
                break;
            }
        }

        if (currentBitLength == 0)
        {
            resultExp = 0;
            resultSign = false;
            var zeros = new uint[maxWords];
            context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), zeros, maxWords, stream);
            return;
        }

        int targetBitLength = maxWords * 32;
        int shift = targetBitLength - currentBitLength;
        int wordShift = shift / 32;
        int bitShift = shift % 32;

        if (bitShift < 0)
        {
            wordShift--;
            bitShift += 32;
        }

        resultExp = valueExp - shift;
        resultSign = valueSign;

        int blockDimX = 256;
        int gridDimX = (maxWords + blockDimX - 1) / blockDimX;

        GpuBigFloatKernelsWrapper.LaunchFloatNormalizeKernelPtr(
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords,
            IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), maxWords,
            wordShift, bitShift,
            (uint)gridDimX, 1, 1, (uint)blockDimX, 1, 1,
            0, stream);
    }

    public void Add(ref BigIntExecutionContext context,
             BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
             BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
             BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var stream = context.Stream;
        int maxWords = resultSig.NumWords;

        bool leftLarger = leftExp > rightExp || (leftExp == rightExp && leftSig.NumWords >= rightSig.NumWords);
        int expDiff = leftLarger ? (leftExp - rightExp) : (rightExp - leftExp);

        var largerSig = leftLarger ? leftSig : rightSig;
        var smallerSig = leftLarger ? rightSig : leftSig;
        bool largerSign = leftLarger ? leftSign : rightSign;
        bool smallerSign = leftLarger ? rightSign : leftSign;

        // Create GPU integers
        using var largerOperand = new GpuUIntBigInteger(context.GpuContext, largerSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(largerOperand.DevicePointer, IntPtr.Add(largerSig.Buffer.Pointer, largerSig.WordOffset * 4), largerSig.NumWords, stream);

        using var smallerOperand = new GpuUIntBigInteger(context.GpuContext, smallerSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(smallerOperand.DevicePointer, IntPtr.Add(smallerSig.Buffer.Pointer, smallerSig.WordOffset * 4), smallerSig.NumWords, stream);

        int shiftWords = expDiff / 32 + 1;
        int shiftedWords = largerSig.NumWords + shiftWords;

        using var shiftedLarger = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(shiftedLarger.DevicePointer, shiftedWords, stream);
        
        context.GpuContext.Provider.ShiftLeft(context.GpuContext, largerOperand, expDiff, shiftedLarger);

        int tempWords = System.Math.Max(shiftedWords, smallerSig.NumWords) + 1;
        using var tempResult = context.GpuContext.RentTemp(tempWords);
        context.GpuContext.Provider.ClearMemoryAsync(tempResult.DevicePointer, tempWords, stream);

        if (largerSign == smallerSign)
        {
            context.GpuContext.Provider.Add(context.GpuContext, shiftedLarger, smallerOperand, tempResult);
            resultSign = largerSign;
        }
        else
        {
            int cmp = context.GpuContext.Provider.CompareTo(context.GpuContext, shiftedLarger, smallerOperand);
            if (cmp >= 0)
            {
                context.GpuContext.Provider.Sub(context.GpuContext, shiftedLarger, smallerOperand, tempResult);
                resultSign = largerSign;
            }
            else
            {
                context.GpuContext.Provider.Sub(context.GpuContext, smallerOperand, shiftedLarger, tempResult);
                resultSign = smallerSign;
            }
        }

        bool isZero = context.GpuContext.Provider.IsZero(context.GpuContext, tempResult);
        if (isZero)
        {
            resultSign = false;
        }

        var tempHandle = new BigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempWords);
        int baseExp = leftLarger ? rightExp : leftExp;

        Normalize(ref context, tempHandle, baseExp, resultSign, resultSig, out resultExp, out resultSign);
    }

    public void Multiply(ref BigIntExecutionContext context,
                  BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                  BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                  BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var stream = context.Stream;
        int tempWords = leftSig.NumWords + rightSig.NumWords;

        using var leftOperand = new GpuUIntBigInteger(context.GpuContext, leftSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(leftOperand.DevicePointer, IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, stream);

        using var rightOperand = new GpuUIntBigInteger(context.GpuContext, rightSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(rightOperand.DevicePointer, IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, stream);

        using var tempResult = context.GpuContext.RentTemp(tempWords);
        context.GpuContext.Provider.ClearMemoryAsync(tempResult.DevicePointer, tempWords, stream);

        context.GpuContext.Provider.Mul(context.GpuContext, leftOperand, rightOperand, tempResult);

        var tempHandle = new BigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempWords);
        
        bool tempSign = leftSign != rightSign; 
        int tempExp = leftExp + rightExp;

        // Determine if product is zero
        bool isZero = context.GpuContext.Provider.IsZero(context.GpuContext, tempResult);
        if (isZero)
        {
            tempSign = false;
        }

        Normalize(ref context, tempHandle, tempExp, tempSign, resultSig, out resultExp, out resultSign);
    }

    public void Divide(ref BigIntExecutionContext context, 
                BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Reciprocal(ref BigIntExecutionContext context, 
                    BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                    BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Sqrt(ref BigIntExecutionContext context, 
              BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
              BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Rsqrt(ref BigIntExecutionContext context, 
               BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
               BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
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
