using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;
using Kassyi.GSharp.Stdlib.Math.Interop;

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

        var dA = IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4);
        var dResult = IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4);

        if (context.GpuContext.Provider is Kassyi.GSharp.Stdlib.Math.BigInt.Providers.NativeProvider)
        {
            if (!NativeMethods.GpuBigFloat_Normalize(
                    context.GpuContext.WorkspacePointer,
                    dA, valueSig.NumWords,
                    dResult, maxWords,
                    valueExp,
                    out var outExp,
                    out var isZero,
                    stream))
            {
                throw new InvalidOperationException("Native GpuBigFloat_Normalize failed.");
            }

            resultExp = outExp;
            resultSign = isZero == 0 && valueSign;
            return;
        }

        // Fallback for CPU / Interpreter modes
        CSharpBigFloatArithmetic.Instance.Normalize(ref context, valueSig, valueExp, valueSign, resultSig, out resultExp, out resultSign);
    }

    public void Add(ref BigIntExecutionContext context,
        BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
        BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var stream = context.Stream;
        var leftLarger = leftExp > rightExp || (leftExp == rightExp && leftSig.NumWords >= rightSig.NumWords);
        var expDiff = System.Math.Abs(leftExp - rightExp);
        var shiftWords = expDiff / 32 + 1;
        var maxSize = System.Math.Max(leftSig.NumWords, rightSig.NumWords) + shiftWords + 1;

        if (context.GpuContext.Provider is Kassyi.GSharp.Stdlib.Math.BigInt.Providers.NativeProvider)
        {
            // Allocate workspace buffers
            using var shiftedBuffer = context.GpuContext.RentTemp(maxSize);
            using var tempResult = context.GpuContext.RentTemp(maxSize);

            if (!NativeMethods.GpuBigFloat_AddSub(
                    context.GpuContext.WorkspacePointer,
                    IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, leftExp, leftSign ? 1 : 0,
                    IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, rightExp, rightSign ? 1 : 0,
                    0, // isSubtract is 0 since subtraction is handled via sign flip
                    shiftedBuffer.DevicePointer,
                    tempResult.DevicePointer,
                    IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), resultSig.NumWords,
                    out var outExp, out var outSign, out var isZero, stream))
            {
                throw new InvalidOperationException("Native GpuBigFloat_AddSub failed.");
            }

            resultExp = outExp;
            resultSign = isZero == 0 && outSign != 0;
            return;
        }

        // Fallback for CPU / Interpreter modes
        CSharpBigFloatArithmetic.Instance.Add(ref context, leftSig, leftExp, leftSign, rightSig, rightExp, rightSign, resultSig, out resultExp, out resultSign);
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
        if (context.GpuContext.Provider is not GSharpAotProvider && context.GpuContext.Provider is not NativeProvider)
        {
            CSharpBigFloatArithmetic.Instance.Reciprocal(ref context, valueSig, valueExp, valueSign, resultSig, out resultExp, out resultSign);
            return;
        }

        var stream = context.Stream;

        using var valueOperand = new GpuUIntBigInteger(context.GpuContext, valueSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(valueOperand.DevicePointer,
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, stream);

        var marginWords = 2; // Provide a small margin
        var reciprocalWords = 2 * valueSig.NumWords + marginWords;
        using var reciprocalResult = context.GpuContext.RentTemp(reciprocalWords);
        context.GpuContext.Provider.ClearMemoryAsync(reciprocalResult.DevicePointer, reciprocalWords, stream);

        if (context.GpuContext.Provider is NativeProvider)
        {
            NativeMethods.GpuBigFloat_Reciprocal(context.GpuContext.WorkspacePointer, valueOperand.DevicePointer,
                reciprocalResult.DevicePointer, valueSig.NumWords, marginWords, stream);
        }
        else
        {
            // Compute Reciprocal using Newton-Raphson from GpuBigIntDivRem
            GpuBigIntDivRem.Reciprocal(valueOperand, reciprocalResult, marginWords);
        }

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

        if (context.GpuContext.Provider is not GSharpAotProvider && context.GpuContext.Provider is not NativeProvider)
        {
            CSharpBigFloatArithmetic.Instance.Sqrt(ref context, valueSig, valueExp, valueSign, resultSig, out resultExp, out resultSign);
            return;
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

    public void DivideByUInt32(ref BigIntExecutionContext context,
        BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
        uint divisor,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign, out uint remainder)
    {
        resultExp = valueExp;
        resultSign = valueSign;

        var deviceA = IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4);
        var deviceResult = IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4);

        var success = NativeMethods.GpuBigFloat_DivideByUInt32(
            context.GpuContext.WorkspacePointer, deviceA, deviceResult, valueSig.NumWords, divisor, out remainder,
            context.Stream);

        if (!success)
        {
            throw new InvalidOperationException("Native DivideByUInt32 failed (possibly division by zero).");
        }
    }
}
