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
        var stream = context.Stream;
        int maxWords = resultSig.NumWords;

        using var leftOperand = new GpuUIntBigInteger(context.GpuContext, leftSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(leftOperand.DevicePointer, IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, stream);

        using var rightOperand = new GpuUIntBigInteger(context.GpuContext, rightSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(rightOperand.DevicePointer, IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, stream);

        // Shift left by resultSig.NumWords * 32
        int shiftBits = resultSig.NumWords * 32;
        int shiftedWords = leftSig.NumWords + resultSig.NumWords;
        using var shiftedLeft = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(shiftedLeft.DevicePointer, shiftedWords, stream);
        context.GpuContext.Provider.ShiftLeft(context.GpuContext, leftOperand, shiftBits, shiftedLeft);

        // DivRem expects all buffers to be the same size in current implementation, 
        // wait, GpuBigIntDivRem.DivRem requires left, right, quotient to be same size!
        // Let's check GpuBigIntDivRem.DivRem requirements: 
        // ThrowIfSizeMismatch(leftOperand, rightOperand, quotientBuffer);
        // ThrowIfSizeMismatch(leftOperand, remainderBuffer);
        
        int alignWords = shiftedWords;
        
        using var alignedRight = context.GpuContext.RentTemp(alignWords);
        context.GpuContext.Provider.ClearMemoryAsync(alignedRight.DevicePointer, alignWords, stream);
        context.GpuContext.Provider.CopyDeviceToDevice(alignedRight.DevicePointer, rightOperand.DevicePointer, rightOperand.NumWords, stream);

        using var quotientBuffer = context.GpuContext.RentTemp(alignWords);
        using var remainderBuffer = context.GpuContext.RentTemp(alignWords);

        GpuBigIntDivRem.DivRem(shiftedLeft, alignedRight, quotientBuffer, remainderBuffer);

        var tempHandle = new BigIntMemoryHandle(quotientBuffer.Handle.Buffer, quotientBuffer.Handle.WordOffset, alignWords);
        
        bool tempSign = leftSign != rightSign;
        int tempExp = leftExp - rightExp - shiftBits;

        // Check if zero
        bool isZero = context.GpuContext.Provider.IsZero(context.GpuContext, quotientBuffer);
        if (isZero)
        {
            tempSign = false;
        }

        Normalize(ref context, tempHandle, tempExp, tempSign, resultSig, out resultExp, out resultSign);
    }

    public void Reciprocal(ref BigIntExecutionContext context, 
                    BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                    BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var stream = context.Stream;
        int maxWords = resultSig.NumWords;

        using var valueOperand = new GpuUIntBigInteger(context.GpuContext, valueSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(valueOperand.DevicePointer, IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, stream);

        int marginWords = 2; // Provide a small margin
        int reciprocalWords = 2 * valueSig.NumWords + marginWords;
        using var reciprocalResult = context.GpuContext.RentTemp(reciprocalWords);
        context.GpuContext.Provider.ClearMemoryAsync(reciprocalResult.DevicePointer, reciprocalWords, stream);

        // Compute Reciprocal using Newton-Raphson from GpuBigIntDivRem
        GpuBigIntDivRem.Reciprocal(valueOperand, reciprocalResult, marginWords);

        var tempHandle = new BigIntMemoryHandle(reciprocalResult.Handle.Buffer, reciprocalResult.Handle.WordOffset, reciprocalWords);
        
        resultSign = valueSign;
        
        // The Newton-Raphson reciprocal computes 2^(2 * numBits + marginBits) / valueOperand.
        // So the exponent of the result is effectively shifted.
        // We set tempExp based on CPU reference equivalent shifting: 
        // CPU shifts 1 by (valueSig.NumWords + resultSig.NumWords) * 32.
        // Wait, the result of GpuBigIntDivRem.Reciprocal is equivalent to (1 << (2*numBits+marginBits)) / valueOperand.
        // So mathematically, result * 2^{-(2*numBits+marginBits)} ~ 1 / valueOperand.
        int shiftBits = (2 * valueSig.NumWords + marginWords) * 32;
        int tempExp = -valueExp - shiftBits;

        Normalize(ref context, tempHandle, tempExp, resultSign, resultSig, out resultExp, out resultSign);
    }

    public void Sqrt(ref BigIntExecutionContext context, 
              BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
              BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (valueSign)
            throw new InvalidOperationException("Cannot compute square root of a negative number.");
            
        var stream = context.Stream;
        int e = valueExp;
        
        using var valueOperand = new GpuUIntBigInteger(context.GpuContext, valueSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(valueOperand.DevicePointer, IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, stream);

        // Check for zero
        bool isZero = context.GpuContext.Provider.IsZero(context.GpuContext, valueOperand);
        if (isZero)
        {
            resultExp = 0;
            resultSign = false;
            var zeros = new uint[resultSig.NumWords];
            context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), zeros, resultSig.NumWords, stream);
            return;
        }

        // CPU equivalent scaling approach:
        // We shift valueOperand left by (resultSig.NumWords * 32 * 2).
        // Since we also may shift left by 1 if e % 2 != 0, the total shift is 1 + (resultSig.NumWords * 32 * 2).
        int scaleShift = resultSig.NumWords * 32 * 2;
        int totalShift = scaleShift;
        if (e % 2 != 0)
        {
            totalShift += 1;
            e--;
        }

        int shiftedWords = valueSig.NumWords + (totalShift / 32) + 1;
        using var largeOperand = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(largeOperand.DevicePointer, shiftedWords, stream);
        context.GpuContext.Provider.CopyDeviceToDevice(largeOperand.DevicePointer, valueOperand.DevicePointer, valueSig.NumWords, stream);

        using var shiftedOperand = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(shiftedOperand.DevicePointer, shiftedWords, stream);
        
        context.GpuContext.Provider.ShiftLeft(context.GpuContext, largeOperand, totalShift, shiftedOperand);

        // Result of Sqrt is roughly half the bits, so shiftedWords size is enough.
        using var sqrtResult = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(sqrtResult.DevicePointer, shiftedWords, stream);
        
        GpuBigIntDivRem.Sqrt(shiftedOperand, sqrtResult);
        
        var tempHandle = new BigIntMemoryHandle(sqrtResult.Handle.Buffer, sqrtResult.Handle.WordOffset, shiftedWords);
        
        int tempExp = e / 2 - (scaleShift / 2);
        resultSign = false;

        Normalize(ref context, tempHandle, tempExp, resultSign, resultSig, out resultExp, out resultSign);
    }

    public void Rsqrt(ref BigIntExecutionContext context, 
               BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
               BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        if (valueSign)
            throw new InvalidOperationException("Cannot compute inverse square root of a negative number.");
            
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
