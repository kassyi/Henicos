namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Execution provider implementing arbitrary-precision floating-point arithmetic via legacy pre-compiled native CUDA C++ dynamic libraries.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Interops directly with legacy C++/CUDA native entry points (<see cref="NativeMethods"/>)
/// for transitional parity validation and benchmark comparison.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Direct P/Invoke bindings communicate with unmanaged GPU memory without managed array allocation overhead.
/// </para>
/// </remarks>
[Obsolete("Legacy native CUDA provider is maintained for transitional parity and benchmarking only.")]
internal sealed class NativeFloatProvider(IGpuBigFloatArithmetic fallbackStrategy) : IGpuBigFloatArithmetic
{
    /// <summary>
    /// Gets the default singleton instance of <see cref="NativeFloatProvider"/> using <see cref="CSharpCpuBigFloatArithmetic.Instance"/> as fallback.
    /// </summary>
    public static readonly NativeFloatProvider Instance = new(CSharpCpuBigFloatArithmetic.Instance);

    /// <inheritdoc/>
    public GpuFloatResult Normalize(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
        var valueSig = value.Significand;
        return !NativeMethods.GpuBigFloat_Normalize(
            context.GpuWorkspace.WorkspacePointer,
            valueSig.DevicePointer, valueSig.NumWords,
            resultSig.DevicePointer, resultSig.NumWords,
            value.Exponent,
            out var outExp,
            out var isZero,
            context.Stream)
            ? throw new InvalidOperationException("Native GpuBigFloat_Normalize failed.")
            : new GpuFloatResult(outExp, isZero == 0 && value.IsNegative);
    }

    /// <inheritdoc/>
    public GpuFloatResult Add(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand left,
        in GpuFloatOperand right,
        GpuBigIntMemoryHandle resultSig)
    {
        var stream = context.Stream;
        var leftSig = left.Significand;
        var rightSig = right.Significand;
        var expDiff = Math.Abs(left.Exponent - right.Exponent);
        var shiftWords = expDiff / 32 + 1;
        var maxSize = Math.Max(leftSig.NumWords, rightSig.NumWords) + shiftWords + 1;

        using var shiftedBuffer = context.GpuWorkspace.RentTemp(maxSize);
        using var tempResult = context.GpuWorkspace.RentTemp(maxSize);

        if (!NativeMethods.GpuBigFloat_AddSub(
                context.GpuWorkspace.WorkspacePointer,
                leftSig.DevicePointer, leftSig.NumWords, left.Exponent,
                left.IsNegative ? 1 : 0,
                rightSig.DevicePointer, rightSig.NumWords, right.Exponent,
                right.IsNegative ? 1 : 0,
                0, // isSubtract is 0 since subtraction is handled via sign flip
                shiftedBuffer.DevicePointer,
                tempResult.DevicePointer,
                resultSig.DevicePointer, resultSig.NumWords,
                out var outExp, out var outSign, out var isZero, stream))
        {
            throw new InvalidOperationException("Native GpuBigFloat_AddSub failed.");
        }

        return new GpuFloatResult(outExp, isZero == 0 && outSign != 0);
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
        var stream = context.Stream;
        var tempExp = left.Exponent + right.Exponent;
        var tempSign = (left.IsNegative != right.IsNegative) ? 1 : 0;

        return !NativeMethods.GpuBigFloat_Multiply(
            context.GpuWorkspace.WorkspacePointer,
            leftSig.DevicePointer, leftSig.NumWords,
            rightSig.DevicePointer, rightSig.NumWords,
            resultSig.DevicePointer, resultSig.NumWords,
            tempExp, tempSign,
            out var outExp, out var outSign, stream)
            ? throw new InvalidOperationException("Native GpuBigFloat_Multiply failed.")
            : new GpuFloatResult(outExp, outSign != 0);
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
        var rightOperand = rightSig.AsBorrowedBigUInt(context.GpuWorkspace);

        if (rightOperand.IsZero())
        {
            throw new DivideByZeroException("GBF Division by Zero.");
        }

        if (resultSig.NumWords <= 1024)
        {
            const int MarginWords = 2;
            var reciprocalWords = 2 * rightSig.NumWords + MarginWords;
            using var reciprocalBuffer = context.GpuWorkspace.RentTemp(reciprocalWords);

            var recRes = Reciprocal(ref context, in right, reciprocalBuffer.Handle);
            return Multiply(
                ref context,
                in left,
                new GpuFloatOperand(reciprocalBuffer.Handle, recRes.Exponent, recRes.IsNegative),
                resultSig);
        }

        return fallbackStrategy.Divide(ref context, in left, in right, resultSig);
    }

    /// <inheritdoc/>
    public GpuFloatResult Reciprocal(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
        var stream = context.Stream;
        var valueSig = value.Significand;
        var valueOperand = valueSig.AsBorrowedBigUInt(context.GpuWorkspace);
        if (valueOperand.IsZero())
        {
            throw new DivideByZeroException("GBF Reciprocal of Zero.");
        }

        const int MarginWords = 2;
        var reciprocalWords = 2 * valueSig.NumWords + MarginWords;
        using var reciprocalResult = context.GpuWorkspace.RentTemp(reciprocalWords);
        context.GpuWorkspace.Provider.ClearMemoryAsync(reciprocalResult.DevicePointer, reciprocalWords, stream);

        NativeMethods.GpuBigFloat_Reciprocal(context.GpuWorkspace.WorkspacePointer, valueOperand.DevicePointer,
            reciprocalResult.DevicePointer, valueSig.NumWords, MarginWords, stream);

        var shiftBits = reciprocalWords * 32;
        var tempExp = -value.Exponent - shiftBits;

        return Normalize(ref context, new GpuFloatOperand(reciprocalResult.Handle, tempExp, value.IsNegative),
            resultSig);
    }

    /// <inheritdoc/>
    public GpuFloatResult Sqrt(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig) =>
        fallbackStrategy.Sqrt(ref context, in value, resultSig);

    /// <inheritdoc/>
    public GpuFloatResult Rsqrt(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig) =>
        fallbackStrategy.Rsqrt(ref context, in value, resultSig);

    /// <inheritdoc/>
    public GpuFloatResult Exp(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig) =>
        fallbackStrategy.Exp(ref context, in value, resultSig);

    /// <inheritdoc/>
    public GpuFloatResult Log(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig) =>
        fallbackStrategy.Log(ref context, in value, resultSig);

    /// <inheritdoc/>
    public GpuFloatResult Pow(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand baseValue,
        in GpuFloatOperand exponent,
        GpuBigIntMemoryHandle resultSig) =>
        fallbackStrategy.Pow(ref context, in baseValue, in exponent, resultSig);

    /// <inheritdoc/>
    public GpuFloatScalarDivResult DivideByUInt32(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        uint divisor,
        GpuBigIntMemoryHandle resultSig)
    {
        var valueSig = value.Significand;
        var success = NativeMethods.GpuBigFloat_DivideByUInt32(
            context.GpuWorkspace.WorkspacePointer,
            valueSig.DevicePointer,
            resultSig.DevicePointer,
            valueSig.NumWords,
            divisor,
            out var remainder,
            context.Stream);

        if (!success)
        {
            throw new InvalidOperationException("Native DivideByUInt32 failed (possibly division by zero).");
        }

        return new GpuFloatScalarDivResult(value.Exponent, value.IsNegative, remainder);
    }
}
