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

        if (context.GpuContext.Provider is NativeProvider)
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
        var expDiff = System.Math.Abs(leftExp - rightExp);
        var shiftWords = expDiff / 32 + 1;
        var maxSize = System.Math.Max(leftSig.NumWords, rightSig.NumWords) + shiftWords + 1;

        if (context.GpuContext.Provider is NativeProvider)
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
        if (context.GpuContext.Provider is NativeProvider)
        {
            var stream = context.Stream;
            var tempExp = leftExp + rightExp;
            var tempSign = (leftSign != rightSign) ? 1 : 0;

            if (!NativeMethods.GpuBigFloat_Multiply(
                context.GpuContext.WorkspacePointer,
                IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords,
                IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords,
                IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), resultSig.NumWords,
                tempExp, tempSign,
                out var outExp, out var outSign, stream))
            {
                throw new InvalidOperationException("Native GpuBigFloat_Multiply failed.");
            }

            resultExp = outExp;
            resultSign = outSign != 0;
            return;
        }

        // Fallback for CPU / Interpreter modes
        CSharpBigFloatArithmetic.Instance.Multiply(ref context, leftSig, leftExp, leftSign, rightSig, rightExp, rightSign, resultSig, out resultExp, out resultSign);
    }

    public void Divide(ref BigIntExecutionContext context,
        BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
        BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
        BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var stream = context.Stream;

        // Create rightOperand for zero-check and fallback path
        using var rightOperand = new GpuUIntBigInteger(context.GpuContext, rightSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(rightOperand.DevicePointer,
            IntPtr.Add(rightSig.Buffer.Pointer, rightSig.WordOffset * 4), rightSig.NumWords, stream);

        // 1. Zero Division Check
        var isRightZero = context.GpuContext.Provider.IsZero(context.GpuContext, rightOperand);
        if (isRightZero)
        {
            throw new DivideByZeroException("GBF Division by Zero.");
        }

        // 2. Hybrid Routing
        // Fast Path: A / B = A * Reciprocal(B)
        // Only if NativeProvider is available and size is within Comba multiplication limits (<= 1024)
        if (context.GpuContext.Provider is NativeProvider && resultSig.NumWords <= 1024)
        {
            var marginWords = 2;
            var reciprocalWords = 2 * rightSig.NumWords + marginWords;
            using var reciprocalBuffer = context.GpuContext.RentTemp(reciprocalWords);
            var reciprocalHandle = new BigIntMemoryHandle(reciprocalBuffer.Handle.Buffer, reciprocalBuffer.Handle.WordOffset, reciprocalWords);

            Reciprocal(ref context, rightSig, rightExp, rightSign, reciprocalHandle, out var reciprocalExp, out var reciprocalSign);

            Multiply(ref context, leftSig, leftExp, leftSign, reciprocalHandle, reciprocalExp, reciprocalSign, resultSig, out resultExp, out resultSign);
            return;
        }

        // ----------------------------------------------------
        // Fallback Path: Existing GpuBigIntDivRem based processing for Mega Division
        // ----------------------------------------------------
        using var leftOperand = new GpuUIntBigInteger(context.GpuContext, leftSig.NumWords);
        context.GpuContext.Provider.CopyDeviceToDevice(leftOperand.DevicePointer,
            IntPtr.Add(leftSig.Buffer.Pointer, leftSig.WordOffset * 4), leftSig.NumWords, stream);

        var shiftBits = resultSig.NumWords * 32 * 2;
        var shiftedWords = leftSig.NumWords + resultSig.NumWords * 2;
        using var shiftedLeft = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(shiftedLeft.DevicePointer, shiftedWords, stream);

        using var paddedLeft = context.GpuContext.RentTemp(shiftedWords);
        context.GpuContext.Provider.ClearMemoryAsync(paddedLeft.DevicePointer, shiftedWords, stream);
        context.GpuContext.Provider.CopyDeviceToDevice(paddedLeft.DevicePointer, leftOperand.DevicePointer, leftOperand.NumWords, stream);

        context.GpuContext.Provider.ShiftLeft(context.GpuContext, paddedLeft, shiftBits, shiftedLeft);

        var quotWords = shiftedWords - rightOperand.NumWords + 1;
        using var quotientBuffer = context.GpuContext.RentTemp(quotWords);
        using var remainderBuffer = context.GpuContext.RentTemp(rightOperand.NumWords);

        GpuBigIntDivRem.DivRem(shiftedLeft, rightOperand, quotientBuffer, remainderBuffer);

        var tempHandle =
            new BigIntMemoryHandle(quotientBuffer.Handle.Buffer, quotientBuffer.Handle.WordOffset, quotWords);

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
            var targetBits = reciprocalWords * 32;
            GpuBigIntDivRem.Reciprocal(valueOperand, reciprocalResult, targetBits, marginWords);
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
