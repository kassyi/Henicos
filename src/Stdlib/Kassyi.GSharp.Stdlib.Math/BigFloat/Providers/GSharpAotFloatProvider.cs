using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Kernels;

namespace Kassyi.GSharp.Stdlib.Math.BigFloat.Providers;

public sealed class GSharpAotFloatProvider : IGpuBigFloatArithmetic
{
    public static readonly GSharpAotFloatProvider Instance = new();

    private GSharpAotFloatProvider()
    {
    }

    public void Normalize(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var stream = context.Stream;
        var maxWords = resultSig.NumWords;

        var valueWords = new uint[valueSig.NumWords];
        context.GpuContext.Provider.CopyDeviceToHost(valueWords,
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, stream);

        var currentBitLength = 0;
        for (var i = valueWords.Length - 1; i >= 0; i--)
        {
            if (valueWords[i] != 0)
            {
                var w = valueWords[i];
                var msb = 0;
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
            context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                zeros, maxWords, stream);
            return;
        }

        var targetBitLength = maxWords * 32;
        var shift = targetBitLength - currentBitLength;
        var wordShift = shift / 32;
        var bitShift = shift % 32;

        if (bitShift < 0)
        {
            wordShift--;
            bitShift += 32;
        }

        resultExp = valueExp - shift;
        resultSign = valueSign;

        var blockDimX = 256;
        var gridDimX = (maxWords + blockDimX - 1) / blockDimX;

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

        var leftLarger = leftExp > rightExp || (leftExp == rightExp && leftSig.NumWords >= rightSig.NumWords);
        var expDiff = leftLarger ? (leftExp - rightExp) : (rightExp - leftExp);

        var largerSig = leftLarger ? leftSig : rightSig;
        var smallerSig = leftLarger ? rightSig : leftSig;
        var largerSign = leftLarger ? leftSign : rightSign;
        var smallerSign = leftLarger ? rightSign : leftSign;

        // Create GPU integers
        using var largerOperand = new GpuUIntBigInteger(context.GpuContext, largerSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(largerOperand.DevicePointer,
            IntPtr.Add(largerSig.Buffer.Pointer, largerSig.WordOffset * 4), largerSig.NumWords, stream);

        using var smallerOperand = new GpuUIntBigInteger(context.GpuContext, smallerSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(smallerOperand.DevicePointer,
            IntPtr.Add(smallerSig.Buffer.Pointer, smallerSig.WordOffset * 4), smallerSig.NumWords, stream);

        var shiftWords = expDiff / 32 + 1;
        var shiftedWords = largerSig.NumWords + shiftWords;

        using var shiftedLarger = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(shiftedLarger.DevicePointer, shiftedWords, stream);

        context.GpuContext.Provider.ShiftLeft(context.GpuContext, largerOperand, expDiff, shiftedLarger);

        var tempWords = System.Math.Max(shiftedWords, smallerSig.NumWords) + 1;
        using var tempResult = context.GpuContext.RentTemp(tempWords);
        context.GpuContext.Provider.ClearMemoryAsync(tempResult.DevicePointer, tempWords, stream);

        if (largerSign == smallerSign)
        {
            context.GpuContext.Provider.Add(context.GpuContext, shiftedLarger, smallerOperand, tempResult);
            resultSign = largerSign;
        }
        else
        {
            var cmp = context.GpuContext.Provider.CompareTo(context.GpuContext, shiftedLarger, smallerOperand);
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

        var isZero = context.GpuContext.Provider.IsZero(context.GpuContext, tempResult);
        if (isZero)
        {
            resultSign = false;
        }

        var tempHandle = new BigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempWords);
        var baseExp = leftLarger ? rightExp : leftExp;

        Normalize(ref context, tempHandle, baseExp, resultSign, resultSig, out resultExp, out resultSign);
    }

    public void Multiply(ref BigIntExecutionContext context,
        BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
        BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var stream = context.Stream;
        var tempWords = leftSig.NumWords + rightSig.NumWords;

        using var leftOperand = new GpuUIntBigInteger(context.GpuContext, leftSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(leftOperand.DevicePointer,
            IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, stream);

        using var rightOperand = new GpuUIntBigInteger(context.GpuContext, rightSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(rightOperand.DevicePointer,
            IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, stream);

        using var tempResult = context.GpuContext.RentTemp(tempWords);
        context.GpuContext.Provider.ClearMemoryAsync(tempResult.DevicePointer, tempWords, stream);

        context.GpuContext.Provider.Mul(context.GpuContext, leftOperand, rightOperand, tempResult);

        var tempHandle = new BigIntMemoryHandle(tempResult.Handle.Buffer, tempResult.Handle.WordOffset, tempWords);

        var tempSign = leftSign != rightSign;
        var tempExp = leftExp + rightExp;

        // Determine if product is zero
        var isZero = context.GpuContext.Provider.IsZero(context.GpuContext, tempResult);
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

        using var leftOperand = new GpuUIntBigInteger(context.GpuContext, leftSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(leftOperand.DevicePointer,
            IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, stream);

        using var rightOperand = new GpuUIntBigInteger(context.GpuContext, rightSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(rightOperand.DevicePointer,
            IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, stream);

        // Shift left by resultSig.NumWords * 32
        var shiftBits = resultSig.NumWords * 32;
        var shiftedWords = leftSig.NumWords + resultSig.NumWords;
        using var shiftedLeft = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(shiftedLeft.DevicePointer, shiftedWords, stream);
        context.GpuContext.Provider.ShiftLeft(context.GpuContext, leftOperand, shiftBits, shiftedLeft);

        var alignWords = shiftedWords;

        using var alignedRight = context.GpuContext.RentTemp(alignWords);
        context.GpuContext.Provider.ClearMemoryAsync(alignedRight.DevicePointer, alignWords, stream);
        context.GpuContext.Provider.CopyDeviceToDevice(alignedRight.DevicePointer, rightOperand.DevicePointer,
            rightOperand.NumWords, stream);

        using var quotientBuffer = context.GpuContext.RentTemp(alignWords);
        using var remainderBuffer = context.GpuContext.RentTemp(alignWords);

        GpuBigIntDivRem.DivRem(shiftedLeft, alignedRight, quotientBuffer, remainderBuffer);

        var tempHandle =
            new BigIntMemoryHandle(quotientBuffer.Handle.Buffer, quotientBuffer.Handle.WordOffset, alignWords);

        var tempSign = leftSign != rightSign;
        var tempExp = leftExp - rightExp - shiftBits;

        // Check if zero
        var isZero = context.GpuContext.Provider.IsZero(context.GpuContext, quotientBuffer);
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

        using var valueOperand = new GpuUIntBigInteger(context.GpuContext, valueSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(valueOperand.DevicePointer,
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, stream);

        var marginWords = 2; // Provide a small margin
        var reciprocalWords = 2 * valueSig.NumWords + marginWords;
        using var reciprocalResult = context.GpuContext.RentTemp(reciprocalWords);
        context.GpuContext.Provider.ClearMemoryAsync(reciprocalResult.DevicePointer, reciprocalWords, stream);

        // Compute Reciprocal using Newton-Raphson from GpuBigIntDivRem
        GpuBigIntDivRem.Reciprocal(valueOperand, reciprocalResult, marginWords);

        var tempHandle = new BigIntMemoryHandle(reciprocalResult.Handle.Buffer, reciprocalResult.Handle.WordOffset,
            reciprocalWords);

        resultSign = valueSign;
        var shiftBits = (2 * valueSig.NumWords + marginWords) * 32;
        var tempExp = -valueExp - shiftBits;

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

        var stream = context.Stream;
        var e = valueExp;

        using var valueOperand = new GpuUIntBigInteger(context.GpuContext, valueSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(valueOperand.DevicePointer,
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, stream);

        var isZero = context.GpuContext.Provider.IsZero(context.GpuContext, valueOperand);
        if (isZero)
        {
            resultExp = 0;
            resultSign = false;
            var zeros = new uint[resultSig.NumWords];
            context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4),
                zeros, resultSig.NumWords, stream);
            return;
        }

        var scaleShift = resultSig.NumWords * 32 * 2;
        var totalShift = scaleShift;
        if (e % 2 != 0)
        {
            totalShift += 1;
            e--;
        }

        var shiftedWords = valueSig.NumWords + (totalShift / 32) + 1;
        using var largeOperand = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(largeOperand.DevicePointer, shiftedWords, stream);
        context.GpuContext.Provider.CopyDeviceToDevice(largeOperand.DevicePointer, valueOperand.DevicePointer,
            valueSig.NumWords, stream);

        using var shiftedOperand = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(shiftedOperand.DevicePointer, shiftedWords, stream);

        context.GpuContext.Provider.ShiftLeft(context.GpuContext, largeOperand, totalShift, shiftedOperand);

        using var sqrtResult = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(sqrtResult.DevicePointer, shiftedWords, stream);

        GpuBigIntDivRem.Sqrt(shiftedOperand, sqrtResult);

        var tempHandle = new BigIntMemoryHandle(sqrtResult.Handle.Buffer, sqrtResult.Handle.WordOffset, shiftedWords);

        var tempExp = e / 2 - (scaleShift / 2);
        resultSign = false;

        Normalize(ref context, tempHandle, tempExp, resultSign, resultSig, out resultExp, out resultSign);
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
        using var sqrtResult = context.GpuContext.RentTemp(tempWords);
        var sqrtHandle = new BigIntMemoryHandle(sqrtResult.Handle.Buffer, sqrtResult.Handle.WordOffset, tempWords);

        Sqrt(ref context, valueSig, valueExp, valueSign, sqrtHandle, out var sqrtExp, out var sqrtSign);

        Reciprocal(ref context, sqrtHandle, sqrtExp, sqrtSign, resultSig, out resultExp, out resultSign);
    }

    public void Exp(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        // TODO: Implement fully native GPU kernel for Exp (Taylor series with GPU buffers)
        // Currently delegates to CPU fallback since scalar division on GPU is not exposed.
        CSharpBigFloatArithmetic.Instance.Exp(ref context, valueSig, valueExp, valueSign, resultSig, out resultExp,
            out resultSign);
    }

    public void Log(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        // TODO: Implement fully native GPU kernel for Log (AGM with GPU buffers)
        CSharpBigFloatArithmetic.Instance.Log(ref context, valueSig, valueExp, valueSign, resultSig, out resultExp,
            out resultSign);
    }

    public void Pow(ref BigIntExecutionContext context,
        BigIntMemoryHandle baseSig, int baseExp, bool baseSign,
        BigIntMemoryHandle expSig, int exponentExp, bool exponentSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        // TODO: Implement Pow natively
        CSharpBigFloatArithmetic.Instance.Pow(ref context, baseSig, baseExp, baseSign, expSig, exponentExp,
            exponentSign, resultSig, out resultExp, out resultSign);
    }
}
