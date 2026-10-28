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
/// <para>
/// <b>Transcendental functions:</b> <see cref="Exp"/>, <see cref="Log"/> and <see cref="Pow"/> run on the GPU with the
/// host-side plan and the integer steps of <see cref="CSharpCpuBigFloatArithmetic"/>, so both return the same bits: the
/// exact value rounded toward zero, accepted by the rounding check of Ziv and recomputed with more guard words when the
/// error bound straddles a <c>p</c>-bit value. Exp reduces <c>x = k·ln 2 + r</c> and evaluates a Paterson–Stockmeyer
/// Taylor polynomial at <c>r / 2^s</c> followed by <c>s</c> squarings; Log reduces <c>x = m·2^e</c> and evaluates an
/// <c>atanh</c> series in Horner or Paterson–Stockmeyer form; Pow returns an exactly representable power directly and
/// computes every other one as <c>e^(y·L)</c>. These algorithms serve every precision (faster ones for large precisions
/// are not implemented); <see cref="GpuWorkspace.Transcendental"/> tunes them. In CPU fallback mode every operation,
/// Pow included, goes to the fallback strategy.
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
        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Normalize(ref context, in value, resultSig);
        }

        return NormalizeScaled(context.GpuWorkspace, value.Significand, value.Exponent, value.IsNegative, resultSig);
    }

    // Normalizes significand·2^exponent into resultSig (RZ) and returns the exponent and the sign of the result. The
    // exponent is computed in long and checked into int at the end (#661 §4.1): an intermediate exponent outside int is
    // fine when the result fits, and a result exponent outside int throws OverflowException instead of wrapping around.
    private static GpuFloatResult NormalizeScaled(
        GpuWorkspace workspace, GpuBigIntMemoryHandle significand, long exponent, bool isNegative,
        GpuBigIntMemoryHandle resultSig)
    {
        var valueView = significand.AsView(workspace);
        var resultView = resultSig.AsView(workspace);
        var shift = GpuBigIntMath.NormalizeToMsb(valueView, resultView, out var actualBits);
        if (actualBits == 0)
        {
            return new GpuFloatResult(0, false);
        }

        return new GpuFloatResult(checked((int)(exponent - shift)), isNegative);
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

        var leftView = leftSig.AsView(context.GpuWorkspace);
        var rightView = rightSig.AsView(context.GpuWorkspace);
        var prodWords = leftSig.NumWords + rightSig.NumWords;
        using var prodBuffer = context.GpuWorkspace.RentTemp(prodWords);

        GpuBigIntMath.Mul(leftView, rightView, prodBuffer);

        return NormalizeScaled(context.GpuWorkspace, prodBuffer.Handle, (long)left.Exponent + right.Exponent,
            left.IsNegative != right.IsNegative, resultSig);
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
        var rightOperand = rightSig.AsView(context.GpuWorkspace);

        // 1. Zero Division Check
        if (rightOperand.IsZero())
        {
            throw new DivideByZeroException("GBF Division by Zero.");
        }

        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Divide(ref context, in left, in right, resultSig);
        }

        // A divisor much wider than the result is cut to its top n + 2 words (#661 §4.12), up to the measured width.
        if (context.GpuWorkspace.WideOperands.CutsDivisor(resultSig.NumWords, rightSig.NumWords))
        {
            return DivideByTruncatedDivisor(context.GpuWorkspace, in left, in right, resultSig);
        }

        // 2. G# Kernel / Mega Division Path via GpuBigIntDivRem
        var leftOperand = leftSig.AsView(context.GpuWorkspace);
        if (leftOperand.IsZero())
        {
            context.GpuWorkspace.Provider.ClearMemoryAsync(resultSig.DevicePointer, resultSig.NumWords, context.Stream);
            return new GpuFloatResult(0, false);
        }

        // floor(L·2^s / R) has at least bits(L) + s - bits(R) >= 1 + 32·(n + Rw) - 32·Rw = p + 1 bits for
        // s = 32·(n + Rw), whatever the widths of L and R, so the truncation to p bits is RZ (#661).
        var shiftBits = (resultSig.NumWords + rightSig.NumWords) * 32;
        var shiftedWords = leftSig.NumWords + resultSig.NumWords + rightSig.NumWords;
        using var shiftedLeft = context.GpuWorkspace.RentTemp(shiftedWords);
        GpuBigIntMath.ShiftAndResize(leftOperand, shiftBits, shiftedLeft);

        using var quotientBuffer = context.GpuWorkspace.RentTemp(shiftedWords);
        using var remainderBuffer = context.GpuWorkspace.RentTemp(rightOperand.NumWords);

        GpuBigIntDivRem.DivRem(shiftedLeft, rightOperand, quotientBuffer, remainderBuffer);

        var tempSign = left.IsNegative != right.IsNegative;
        var tempExp = (long)left.Exponent - right.Exponent - shiftBits;

        return NormalizeScaled(context.GpuWorkspace, quotientBuffer.Handle, tempExp, tempSign, resultSig);
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
        var valueOperand = valueSig.AsView(context.GpuWorkspace);
        if (valueOperand.IsZero())
        {
            throw new DivideByZeroException("GBF Reciprocal of Zero.");
        }

        // X = floor(2^T / V) in w = v + n + 1 words with T = 32·w - 1: X <= 2^T < 2^(32·w) fits the buffer even for
        // V = 1, and V < 2^(32·v) gives X >= 2^(T - 32·v), at least p + 32 bits whatever the width v of V, so the
        // truncation to p bits is RZ (#661).
        var reciprocalWords = valueSig.NumWords + resultSig.NumWords + 1;
        var targetBits = reciprocalWords * 32 - 1;
        using var reciprocalResult = context.GpuWorkspace.RentTemp(reciprocalWords);
        context.GpuWorkspace.Provider.ClearMemoryAsync(reciprocalResult.DevicePointer, reciprocalWords, stream);

        GpuBigIntDivRem.Reciprocal(valueOperand, reciprocalResult, targetBits);

        var tempExp = -(long)value.Exponent - targetBits;

        return NormalizeScaled(context.GpuWorkspace, reciprocalResult.Handle, tempExp, value.IsNegative, resultSig);
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

        var workspace = context.GpuWorkspace;
        var valueOperand = value.Significand.AsView(workspace);

        var actualBits = valueOperand.GetBitLength();
        if (actualBits == 0)
        {
            workspace.Provider.ClearMemoryAsync(resultSig.DevicePointer, resultSig.NumWords, context.Stream);
            return new GpuFloatResult(0, false);
        }

        // sqrt(S·2^E) = sqrt(S·2^t)·2^((E - t) / 2). With E - t even and S·2^t in [2^(2p-2), 2^(2p)), the integer square
        // root of A = floor(S·2^t) has exactly p bits and is RZ_p of the root: floor(sqrt(floor(y))) = floor(sqrt(y)), so
        // a negative t may truncate S (#661).
        var n = resultSig.NumWords;
        var shift = 2L * 32 * n - actualBits;
        if ((value.Exponent - shift) % 2 != 0)
        {
            shift--;
        }

        using var scaled = workspace.RentTemp(2 * n);
        GpuBigIntMath.ShiftAndResize(valueOperand, checked((int)shift), scaled);
        GpuBigIntDivRem.SqrtFloor(scaled, resultSig.AsBorrowedBigUInt(workspace));

        // E - t is computed in long and is even; the exponent of the result must fit in int.
        return new GpuFloatResult(checked((int)((value.Exponent - shift) / 2)), false);
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

        var workspace = context.GpuWorkspace;
        var valueOperand = value.Significand.AsView(workspace);

        var actualBits = valueOperand.GetBitLength();
        if (actualBits == 0)
        {
            throw new DivideByZeroException("Cannot compute inverse square root of zero.");
        }

        // 1/sqrt(S·2^E) = 2^(-(E - t)/2) / sqrt(A) with A = S·2^t of w words, bit length 32w or 32w - 1 (as InverseSqrt
        // needs) and E - t even. A is never truncated: the inverse root decreases, so a smaller radicand would round the
        // result up. When S already fills its words and E is odd, A moves one word up instead (#661).
        var words = value.Significand.NumWords;
        long shift = 32L * words - actualBits;
        if ((value.Exponent - shift) % 2 != 0)
        {
            if (shift > 0)
            {
                shift--;
            }
            else
            {
                words++;
                shift = 31;
            }
        }

        using var scaled = workspace.RentTemp(words);
        GpuBigIntMath.ShiftAndResize(valueOperand, checked((int)shift), scaled);

        // A radicand much wider than the result is cut to its top n + 2 words (#661 §4.12), up to the measured width.
        if (workspace.WideOperands.CutsRadicand(resultSig.NumWords, words))
        {
            return RsqrtOfTruncatedRadicand(workspace, scaled, words, (value.Exponent - shift) / 2, resultSig);
        }

        // x = floor(2^B / sqrt(A)), B = 32(w + m), has at least B - 16w + 1 >= p + 1 bits for m = max(1, n - floor(w/2)),
        // so its truncation to p bits is RZ_p of the inverse root.
        var marginWords = Math.Max(1, resultSig.NumWords - words / 2);
        using var inverse = workspace.RentTemp(words + marginWords);
        GpuBigIntDivRem.InverseSqrt(scaled, inverse, marginWords);

        // The exponents are computed in long; the exponent of the result must fit in int.
        var exponent = -((value.Exponent - shift) / 2) - 32L * (words + marginWords);
        return NormalizeScaled(workspace, inverse.Handle, exponent, false, resultSig);
    }

    // Divide by a divisor R of at least n + 2 + g words (#661 §4.12.3). With k = 32(n + 2) and
    // j = max(0, bits(R) - k), N = floor(L·2^s) has bits(R) + p + 2 bits and
    // Q_hi = floor(floor(N / 2^j) / floor(R / 2^j)) is Q* = floor(N / R) or Q* + 1, where Q* has p + 2 or p + 3 bits
    // and Q_hi < 2^(p + 3) as well (N / 2^j < 2^(k + p + 2) over R / 2^j >= 2^(k - 1)). When the low 2 bits of Q_hi are
    // not both 0, Q_hi rounds to the same p bits as Q*; otherwise Q_hi·R > N (one full-width product) means
    // Q* = Q_hi - 1. Nothing cut (j = 0) leaves Q_hi = Q*.
    private static GpuFloatResult DivideByTruncatedDivisor(GpuWorkspace workspace, in GpuFloatOperand left,
        in GpuFloatOperand right, GpuBigIntMemoryHandle resultSig)
    {
        var leftOperand = left.Significand.AsView(workspace);
        var rightOperand = right.Significand.AsView(workspace);
        var leftBits = leftOperand.GetBitLength();
        if (leftBits == 0)
        {
            workspace.Provider.ClearMemoryAsync(resultSig.DevicePointer, resultSig.NumWords, workspace.Stream);
            return new GpuFloatResult(0, false);
        }

        workspace.WideOperandCounters.AddTruncation();
        var n = resultSig.NumWords;
        var truncatedWords = n + 2;
        var rightBits = rightOperand.GetBitLength();
        var truncation = Math.Max(0, rightBits - 32 * truncatedWords);
        var dividendBits = rightBits + 32 * n + 2;
        var shift = dividendBits - leftBits;

        // floor(floor(L·2^s) / R) = floor(L·2^s / R) also for s < 0, so N may drop low bits of L.
        using var dividend = workspace.RentTemp((dividendBits + 31) / 32);
        GpuBigIntMath.ShiftAndResize(leftOperand, shift, dividend);
        using var truncatedDividend = workspace.RentTemp((dividendBits - truncation + 31) / 32);
        GpuBigIntMath.ShiftAndResize(dividend, -truncation, truncatedDividend);
        using var truncatedDivisor = workspace.RentTemp(truncatedWords);
        GpuBigIntMath.ShiftAndResize(rightOperand, -truncation, truncatedDivisor);

        // Q_hi < 2^(p + 3) fits n + 1 words.
        using var quotient = workspace.RentTemp(n + 1);
        using var remainder = workspace.RentTemp(truncatedWords);
        GpuBigIntDivRem.DivRem(truncatedDividend, truncatedDivisor, quotient, remainder);

        if (truncation > 0 && (workspace.Provider.ReadWord(quotient.DevicePointer, 0, workspace.Stream) & 3) == 0)
        {
            using var product = workspace.RentTemp(quotient.NumWords + rightOperand.NumWords);
            GpuBigIntMath.Mul(quotient, rightOperand, product);
            CorrectTruncatedEstimate(workspace, product, dividend, quotient);
        }

        var exponent = (long)left.Exponent - right.Exponent - shift;
        return NormalizeScaled(workspace, quotient.Handle, exponent, left.IsNegative != right.IsNegative, resultSig);
    }

    // Rsqrt of a radicand A of w >= n + 2 + g words (#661 §4.12.2): A has bit length 32w or 32w - 1, and the value is
    // 2^(-h) / sqrt(A). With K = n + 2, j = 32(w - K), m = ceil(n/2) and B = 32(K + m) + j/2, the inverse root x_hi of
    // the top K words is x* = floor(2^B / sqrt(A)) or x* + 1, of at least p + 33 bits. When its low word is not 0, x_hi
    // rounds to the same p bits as x*; otherwise x_hi²·A > 2^(2B) (one full-width product) means x* = x_hi - 1.
    private static GpuFloatResult RsqrtOfTruncatedRadicand(GpuWorkspace workspace, GpuBigUInt radicand, int words,
        long halfExponent, GpuBigIntMemoryHandle resultSig)
    {
        workspace.WideOperandCounters.AddTruncation();
        var truncatedWords = resultSig.NumWords + 2;
        var marginWords = (resultSig.NumWords + 1) / 2;
        var topWords = radicand.Handle.Slice(words - truncatedWords, truncatedWords).AsView(workspace);
        using var inverse = workspace.RentTemp(truncatedWords + marginWords);
        GpuBigIntDivRem.InverseSqrt(topWords, inverse, marginWords);

        var exponentBits = 32L * (truncatedWords + marginWords) + 16L * (words - truncatedWords);
        if (workspace.Provider.ReadWord(inverse.DevicePointer, 0, workspace.Stream) == 0)
        {
            using var square = workspace.RentTemp(2 * inverse.NumWords);
            GpuBigIntMath.Mul(inverse, inverse, square);
            using var product = workspace.RentTemp(square.NumWords + words);
            GpuBigIntMath.Mul(square, radicand, product);
            var boundBit = checked((int)(2 * exponentBits));
            using var bound = GpuBigUInt.CreatePowerOfTwo(workspace, boundBit / 32 + 1, boundBit);
            CorrectTruncatedEstimate(workspace, product, bound, inverse);
        }

        // The exponents are computed in long; the exponent of the result must fit in int.
        return NormalizeScaled(workspace, inverse.Handle, -halfExponent - exponentBits, false, resultSig);
    }

    // Subtracts 1 from the estimate on the device when product > bound (TruncatedBoundCorrectionKernel, one block),
    // without a readback.
    private static void CorrectTruncatedEstimate(GpuWorkspace workspace, GpuBigUInt product, GpuBigUInt bound,
        GpuBigUInt estimate)
    {
        workspace.WideOperandCounters.AddBoundCheck();
        var words = Math.Max(product.NumWords, bound.NumWords);
        var blockThreads = (uint)Math.Clamp((words + 31) / 32 * 32, 32, 1024);
        GpuBigIntDivRemKernels.LaunchTruncatedBoundCorrectionKernelPtr(product.DevicePointer, product.NumWords,
            bound.DevicePointer, bound.NumWords, estimate.DevicePointer, estimate.NumWords, gridDimX: 1,
            blockDimX: blockThreads, hStream: workspace.Stream);
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

        return GpuBigFloatTranscendentals.Exp(context.GpuWorkspace, in value, resultSig);
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

        if (context.GpuWorkspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            return fallbackStrategy.Pow(ref context, in baseValue, in exponent, resultSig);
        }

        return GpuBigFloatTranscendentals.Pow(context.GpuWorkspace, in baseValue, in exponent, resultSig);
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
}
