namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Execution provider implementing arbitrary-precision floating-point arithmetic via compiled GSharp GPU device kernels and fallback providers.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Dispatches high-precision floating-point operations (<see cref="IGpuBigFloatArithmetic"/>) to compiled G# device kernels
/// (<see cref="GpuBigFloatKernels"/>, <see cref="GpuBigIntMath"/>, <see cref="GpuBigIntDivRem"/>) when GPU acceleration is active,
/// and routes to <see cref="CSharpCpuBigFloatArithmetic"/> in CPU fallback mode.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Scratch buffers are managed deterministically via workspace pools without managed heap allocations.
/// </para>
/// </remarks>
public sealed class GSharpKernelFloatProvider(IGpuBigFloatArithmetic fallbackStrategy)
    : IGpuBigFloatArithmetic
{
    /// <summary>
    /// Gets the default singleton instance of <see cref="GSharpKernelFloatProvider"/> using <see cref="CSharpCpuBigFloatArithmetic.Instance"/> as fallback.
    /// </summary>
    public static readonly GSharpKernelFloatProvider Instance = new(CSharpCpuBigFloatArithmetic.Instance);

    /// <summary>
    /// Creates a provider that uses pre-compiled G# device kernels for high-precision floating-point operations.
    /// </summary>
    /// <returns>A <see cref="GSharpKernelFloatProvider"/> instance configured for G# kernel operations.</returns>
    public static GSharpKernelFloatProvider CreateGSharpKernel() => Instance;

    /// <inheritdoc/>
    public GpuFloatResult Normalize(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
        var valueSig = value.Significand;
        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Normalize(ref context, in value, resultSig);
        }

        var valueView = valueSig.AsBorrowedBigUInt(context.GpuWorkspace);
        var resultView = resultSig.AsBorrowedBigUInt(context.GpuWorkspace);
        var shift = GpuBigIntMath.NormalizeToMsb(valueView, resultView, out var actualBits);
        if (actualBits == 0)
        {
            return new GpuFloatResult(0, false);
        }

        return new GpuFloatResult(value.Exponent - shift, value.IsNegative);
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

        if (context.GpuWorkspace.Provider is GSharpKernelProvider && !GpuKernelLoader.IsCpuFallbackMode)
        {
            var leftHasLargerExp = left.Exponent > right.Exponent
                                   || (left.Exponent == right.Exponent && leftSig.NumWords >= rightSig.NumWords);
            var larger = leftHasLargerExp ? left : right;
            var smaller = leftHasLargerExp ? right : left;
            var largerSig = larger.Significand;
            var smallerSig = smaller.Significand;

            int signScale;
            bool resultSign;
            if (larger.IsNegative == smaller.IsNegative)
            {
                signScale = 1;
                resultSign = larger.IsNegative;
            }
            else
            {
                using var largerUFloat =
                    new GpuBigUFloat(largerSig.AsBorrowedBigUInt(context.GpuWorkspace), larger.Exponent);
                using var smallerUFloat =
                    new GpuBigUFloat(smallerSig.AsBorrowedBigUInt(context.GpuWorkspace), smaller.Exponent);
                var magCmp = GpuBigNumberAlgorithms.Compare(largerUFloat, smallerUFloat);
                switch (magCmp)
                {
                    case 0:
                        context.GpuWorkspace.Provider.ClearMemoryAsync(resultSig.DevicePointer, resultSig.NumWords,
                            stream);
                        return new GpuFloatResult(0, false);
                    case > 0:
                        // (largerSig << expDiff) - smallerSig => valB - valA (sign == -2)
                        signScale = -2;
                        resultSign = larger.IsNegative;
                        break;
                    default:
                        // smallerSig - (largerSig << expDiff) => valA - valB (sign == -1)
                        signScale = -1;
                        resultSign = smaller.IsNegative;
                        break;
                }
            }

            var rightShift = -expDiff;
            var wordShift = rightShift / 32;
            var bitShift = rightShift % 32;
            if (bitShift < 0)
            {
                wordShift--;
                bitShift += 32;
            }

            var alignedWords = Math.Max(smallerSig.NumWords, largerSig.NumWords + ((expDiff + 31) / 32)) + 1;
            using var tempResult = context.GpuWorkspace.RentTemp(alignedWords);

            var blockThreads = (uint)Math.Clamp(((alignedWords + 31) / 32) * 32, 32, 1024);
            var kernelCtx = new GpuBigFloatKernels.AotContexts.GpuBigFloatAdd
            {
                LargerSig = smallerSig.DevicePointer,
                LargerWords = smallerSig.NumWords,
                SmallerSig = largerSig.DevicePointer,
                SmallerWords = largerSig.NumWords,
                ResultSig = tempResult.DevicePointer,
                NumWords = alignedWords,
                WordShift = wordShift,
                BitShift = bitShift,
                SignScale = signScale
            };

            GpuBigFloatKernels.LaunchFloatAddAlignedKernelPtr(
                kernelCtx, gridDimX: 1, blockDimX: blockThreads, hStream: stream);

            return Normalize(ref context, new GpuFloatOperand(tempResult.Handle, smaller.Exponent, resultSign),
                resultSig);
        }

        return fallbackStrategy.Add(ref context, in left, in right, resultSig);
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

        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Multiply(ref context, in left, in right, resultSig);
        }

        var leftView = leftSig.AsBorrowedBigUInt(context.GpuWorkspace);
        var rightView = rightSig.AsBorrowedBigUInt(context.GpuWorkspace);
        var prodWords = leftSig.NumWords + rightSig.NumWords;
        using var prodBuffer = context.GpuWorkspace.RentTemp(prodWords);

        GpuBigIntMath.Mul(leftView, rightView, prodBuffer);

        return Normalize(ref context,
            new GpuFloatOperand(prodBuffer.Handle, left.Exponent + right.Exponent, left.IsNegative != right.IsNegative),
            resultSig);
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

        // 1. Zero Division Check
        if (rightOperand.IsZero())
        {
            throw new DivideByZeroException("GBF Division by Zero.");
        }

        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Divide(ref context, in left, in right, resultSig);
        }

        // 2. G# Kernel / Mega Division Path via GpuBigIntDivRem
        var leftOperand = leftSig.AsBorrowedBigUInt(context.GpuWorkspace);
        if (leftOperand.IsZero())
        {
            context.GpuWorkspace.Provider.ClearMemoryAsync(resultSig.DevicePointer, resultSig.NumWords, context.Stream);
            return new GpuFloatResult(0, false);
        }

        var shiftBits = resultSig.NumWords * 32 * 2;
        var shiftedWords = leftSig.NumWords + resultSig.NumWords * 2;
        using var shiftedLeft = context.GpuWorkspace.RentTemp(shiftedWords);
        GpuBigIntMath.ShiftAndResize(leftOperand, shiftBits, shiftedLeft);

        using var quotientBuffer = context.GpuWorkspace.RentTemp(shiftedWords);
        using var remainderBuffer = context.GpuWorkspace.RentTemp(rightOperand.NumWords);

        GpuBigIntDivRem.DivRem(shiftedLeft, rightOperand, quotientBuffer, remainderBuffer);

        var tempSign = left.IsNegative != right.IsNegative;
        var tempExp = left.Exponent - right.Exponent - shiftBits;

        return Normalize(ref context, new GpuFloatOperand(quotientBuffer.Handle, tempExp, tempSign), resultSig);
    }

    /// <inheritdoc/>
    public GpuFloatResult Reciprocal(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Reciprocal(ref context, in value, resultSig);
        }

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

        var targetBits = reciprocalWords * 32;
        GpuBigIntDivRem.Reciprocal(valueOperand, reciprocalResult, targetBits, MarginWords);

        var shiftBits = reciprocalWords * 32;
        var tempExp = -value.Exponent - shiftBits;

        return Normalize(ref context, new GpuFloatOperand(reciprocalResult.Handle, tempExp, value.IsNegative),
            resultSig);
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

        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Sqrt(ref context, in value, resultSig);
        }

        var stream = context.Stream;
        var valueOperand = value.Significand.AsBorrowedBigUInt(context.GpuWorkspace);

        var actualBits = valueOperand.GetBitLength();
        if (actualBits == 0)
        {
            context.GpuWorkspace.Provider.ClearMemoryAsync(resultSig.DevicePointer, resultSig.NumWords, stream);
            return new GpuFloatResult(0, false);
        }

        using var invSqrt = ComputeNormalizedInverseSqrt(new InverseSqrtRequest(
            context.GpuWorkspace, valueOperand, value.Exponent, actualBits, resultSig.NumWords));

        var prodWords = invSqrt.NormalizedOperand.NumWords + invSqrt.SlicedInverseSqrt.NumWords;
        using var prodResult = context.GpuWorkspace.RentTemp(prodWords);
        context.GpuWorkspace.Provider.ClearMemoryAsync(prodResult.DevicePointer, prodWords, stream);

        GpuBigIntMath.Mul(invSqrt.NormalizedOperand, invSqrt.SlicedInverseSqrt, prodResult);

        var tempExp = (invSqrt.AdjustedExponent / 2) - invSqrt.EffectiveScaleBits;
        return Normalize(ref context, new GpuFloatOperand(prodResult.Handle, tempExp), resultSig);
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

        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Rsqrt(ref context, in value, resultSig);
        }

        var valueOperand = value.Significand.AsBorrowedBigUInt(context.GpuWorkspace);

        var actualBits = valueOperand.GetBitLength();
        if (actualBits == 0)
        {
            throw new DivideByZeroException("Cannot compute inverse square root of zero.");
        }

        using var invSqrt = ComputeNormalizedInverseSqrt(new InverseSqrtRequest(
            context.GpuWorkspace, valueOperand, value.Exponent, actualBits, resultSig.NumWords));

        var tempExp = -(invSqrt.AdjustedExponent / 2) - invSqrt.EffectiveScaleBits;
        return Normalize(ref context, new GpuFloatOperand(invSqrt.SlicedInverseSqrt.Handle, tempExp), resultSig);
    }

    private static NormalizedInverseSqrtScope ComputeNormalizedInverseSqrt(in InverseSqrtRequest request)
    {
        var workspace = request.Workspace;
        var stream = workspace.Stream;
        var workWords = Math.Max(request.ValueOperand.NumWords, request.TargetResultWords);
        var lz = workWords * 32 - request.ActualBits;
        var adjustedExp = request.ValueExp - lz;
        if (adjustedExp % 2 != 0)
        {
            lz--;
            adjustedExp = request.ValueExp - lz;
        }

        var aNorm = workspace.RentTemp(workWords);
        try
        {
            GpuBigIntMath.ShiftAndResize(request.ValueOperand, lz, aNorm);

            const int MarginWords = 4;
            const int DiscardGuardWords = 2;
            var rsqrtWords = workWords + MarginWords;
            var rsqrtRaw = workspace.RentTemp(rsqrtWords);
            try
            {
                workspace.Provider.ClearMemoryAsync(rsqrtRaw.DevicePointer, rsqrtWords, stream);

                GpuBigIntDivRem.InverseSqrt(aNorm, rsqrtRaw, MarginWords);

                // Add guard-word rounding offset to absorb Newton-Raphson right-shift truncation before slicing off the lowest 2 guard words
                GpuBigIntMath.Add(rsqrtRaw, 4u, rsqrtRaw);

                var slicedHandle = new GpuBigIntMemoryHandle(
                    rsqrtRaw.Handle.Buffer,
                    rsqrtRaw.Handle.WordOffset + DiscardGuardWords,
                    rsqrtWords - DiscardGuardWords);
                var rsqrtSliced = new GpuBigUInt(workspace, slicedHandle, isTemp: true);
                var effectiveScaleBits = (rsqrtWords - DiscardGuardWords) * 32;

                return new NormalizedInverseSqrtScope(aNorm, rsqrtRaw, rsqrtSliced, adjustedExp, effectiveScaleBits);
            }
            catch
            {
                rsqrtRaw.Dispose();
                throw;
            }
        }
        catch
        {
            aNorm.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public GpuFloatResult Exp(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig)
    {
        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Exp(ref context, in value, resultSig);
        }

        var workspace = context.GpuWorkspace;
        var provider = workspace.Provider;
        var stream = context.Stream;
        var valueOperand = value.Significand.AsBorrowedBigUInt(workspace);

        var actualBits = valueOperand.GetBitLength();
        if (actualBits == 0)
        {
            // exp(0) = 1.0 = 0x80000000 * 2^(1 - 32 * NumWords)
            provider.ClearMemoryAsync(resultSig.DevicePointer, resultSig.NumWords, stream);
            provider.WriteWord(resultSig.DevicePointer, resultSig.NumWords - 1, 0x80000000u, stream);
            return new GpuFloatResult(1 - resultSig.NumWords * 32, false);
        }

        if (value.IsNegative)
        {
            // exp(-|x|) = 1 / exp(|x|), avoiding alternating-sign Taylor cancellation
            using var posExpTemp = workspace.RentTemp(resultSig.NumWords + 2);
            var posRes = Exp(ref context, value with { IsNegative = false }, posExpTemp.Handle);
            return Reciprocal(ref context, new GpuFloatOperand(posExpTemp.Handle, posRes.Exponent), resultSig);
        }

        var intBits = Math.Max(0, value.Exponent + actualBits);
        var intWords = Math.Clamp((1 << Math.Min(intBits, 8)) / 20 + 2, 2, 32);
        var fracWords = resultSig.NumWords + 2;
        var shiftBits = fracWords * 32;
        var workWords = fracWords + intWords;

        using var xFixed = workspace.RentTemp(workWords);
        GpuBigIntMath.ShiftAndResize(valueOperand, value.Exponent + shiftBits, xFixed);

        using var sum = workspace.RentTemp(workWords);
        provider.ClearMemoryAsync(sum.DevicePointer, workWords, stream);
        provider.WriteWord(sum.DevicePointer, fracWords, 1u, stream);
        GpuBigIntMath.Add(sum, xFixed, sum);

        using var term = workspace.RentTemp(workWords);
        provider.CopyDeviceToDevice(term.DevicePointer, xFixed.DevicePointer, workWords, stream);

        using var prod = workspace.RentTemp(workWords * 2);

        for (uint k = 2; k <= 1000; k++)
        {
            GpuBigIntMath.Mul(term, xFixed, prod);
            provider.CopyDeviceToDevice(
                term.DevicePointer,
                IntPtr.Add(prod.DevicePointer, fracWords * 4),
                workWords,
                stream);
            GpuBigIntMath.DivideByUInt32(term, k, term);
            if (term.IsZero())
            {
                break;
            }

            GpuBigIntMath.Add(sum, term, sum);
        }

        return Normalize(ref context, new GpuFloatOperand(sum.Handle, -shiftBits), resultSig);
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

        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Log(ref context, in value, resultSig);
        }

        return GpuBigFloatTranscendentals.Log(context.GpuWorkspace, in value, resultSig);
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
        var logRes = Log(ref context, in baseValue, logResult.Handle);

        using var mulResult = context.GpuWorkspace.RentTemp(tempWords);
        var mulRes = Multiply(
            ref context,
            in exponent,
            new GpuFloatOperand(logResult.Handle, logRes.Exponent, logRes.IsNegative),
            mulResult.Handle);

        return Exp(
            ref context,
            new GpuFloatOperand(mulResult.Handle, mulRes.Exponent, mulRes.IsNegative),
            resultSig);
    }

    /// <inheritdoc/>
    public GpuFloatScalarDivResult DivideByUInt32(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        uint divisor,
        GpuBigIntMemoryHandle resultSig)
    {
        var valueSig = value.Significand;
        if (context.GpuWorkspace.Provider is GSharpKernelProvider kernelProvider && !GpuKernelLoader.IsCpuFallbackMode)
        {
            var valueView = valueSig.AsBorrowedBigUInt(context.GpuWorkspace);
            var resultView = resultSig.AsBorrowedBigUInt(context.GpuWorkspace);
            var remainder = kernelProvider.DivideByUInt32(context.GpuWorkspace, valueView, divisor, resultView);
            return new GpuFloatScalarDivResult(value.Exponent, value.IsNegative, remainder);
        }

        return fallbackStrategy.DivideByUInt32(ref context, in value, divisor, resultSig);
    }

    private readonly struct InverseSqrtRequest(
        GpuWorkspace workspace,
        GpuBigUInt valueOperand,
        int valueExp,
        int actualBits,
        int targetResultWords)
    {
        public GpuWorkspace Workspace => workspace;
        public GpuBigUInt ValueOperand => valueOperand;
        public int ValueExp => valueExp;
        public int ActualBits => actualBits;
        public int TargetResultWords => targetResultWords;
    }

    private readonly struct NormalizedInverseSqrtScope(
        [TakesOwnership] GpuBigUInt normalizedOperand,
        [TakesOwnership] GpuBigUInt rawInverseSqrt,
        GpuBigUInt slicedInverseSqrt,
        int adjustedExponent,
        int effectiveScaleBits) : IDisposable
    {
        public GpuBigUInt NormalizedOperand => normalizedOperand;
        public GpuBigUInt RawInverseSqrt => rawInverseSqrt;
        public GpuBigUInt SlicedInverseSqrt => slicedInverseSqrt;
        public int AdjustedExponent => adjustedExponent;
        public int EffectiveScaleBits => effectiveScaleBits;

        public void Dispose()
        {
            normalizedOperand.Dispose();
            rawInverseSqrt.Dispose();
        }
    }
}
