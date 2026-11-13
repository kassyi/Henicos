namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides static mathematical and bitwise operations for <see cref="GpuBigUInt"/> and arbitrary-precision integer calculations on the GPU.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Dispatches high-throughput parallel compute kernels via <see cref="GpuWorkspace.Provider"/>.</para>
/// <para><b>Result widths:</b> Operands may have different word counts (a missing word reads as zero). A result buffer
/// narrower than the minimum of a method is rejected with <see cref="ArgumentException"/>; one at or above the bound of
/// the result receives the exact value and zeros above it. Add, subtract and shift left into a buffer of the operand
/// width are the checked form: a carry, a borrow or a shifted-out bit is a domain error (<see
/// cref="OverflowException"/> at the next read of the workspace stream, or at the call in CPU fallback mode), never
/// dropped.</para>
/// <para><b>Thread Safety:</b> Static methods are thread-safe assuming distinct device memory buffers are supplied for concurrent operations.</para>
/// </remarks>
public static class GpuBigIntMath
{
    [ExcludeFromCodeCoverage] private static void ThrowIfSizeMismatch(GpuBigUInt leftOperand, GpuBigUInt resultBuffer)
    {
        if (leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must have the same size as input buffer. a: {leftOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    /// <summary>
    /// Computes the arbitrary-precision sum of two big integers on the GPU (<c>resultBuffer = leftOperand + rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The first big integer addend.</param>
    /// <param name="rightOperand">The second big integer addend.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the sum.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than the wider operand; nothing
    /// is written.</exception>
    /// <remarks><paramref name="resultBuffer"/> may be an operand or overlap one; the result is the one of a separate
    /// buffer.</remarks>
    public static void Add(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView resultBuffer
    ) =>
        AddCore(leftOperand.AsBorrowedBigUInt(), rightOperand.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt());

    // Add on the owned type; the public method above takes views and borrows them
    private static void AddCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ResultBuffers.ThrowIfNarrower(resultBuffer, Math.Max(leftOperand.NumWords, rightOperand.NumWords), nameof(Add));
        BigIntWideOps.AddSubtract(leftOperand, rightOperand, resultBuffer, subtract: false);
    }

    /// <summary>
    /// Adds a scalar 32-bit unsigned integer to a big integer on the GPU (<c>resultBuffer = leftOperand + rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The big integer addend.</param>
    /// <param name="rightOperand">The 32-bit unsigned scalar addend.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the sum.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> has fewer words than
    /// <paramref name="leftOperand"/>; nothing is written.</exception>
    /// <exception cref="OverflowException">CPU fallback mode only: the sum does not fit in a
    /// <paramref name="resultBuffer"/> as wide as <paramref name="leftOperand"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>Result width:</b> <paramref name="resultBuffer"/> must have at least as many words as
    /// <paramref name="leftOperand"/>. A wider buffer receives the exact sum and zeros above it. A buffer as wide as
    /// the operand is the checked form: a sum that needs another word is a domain error, never dropped.
    /// </para>
    /// <para>
    /// <b>Domain errors:</b> on the GPU, reported as <see cref="OverflowException"/> at the next read of the workspace
    /// stream (or by <see cref="GpuWorkspace.ThrowIfDeviceErrors"/>); in CPU fallback mode, thrown by this call. The
    /// contents of <paramref name="resultBuffer"/> are then unspecified.
    /// </para>
    /// <para>
    /// <paramref name="resultBuffer"/> may be <paramref name="leftOperand"/> itself (in place) or overlap it in any
    /// other way; the result is the one of a separate buffer.
    /// </para>
    /// </remarks>
    public static void Add(scoped GpuBigUIntView leftOperand, uint rightOperand, scoped GpuBigUIntView resultBuffer) =>
        AddCore(leftOperand.AsBorrowedBigUInt(), rightOperand, resultBuffer.AsBorrowedBigUInt());

    // Add on the owned type; the public method above takes views and borrows them
    private static void AddCore(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer) =>
        WriteElementwise(leftOperand, resultBuffer, rightOperand, static (scalar, operand, result) =>
            operand.Workspace.Provider.Add(operand.Workspace, operand, scalar, result));

    /// <summary>
    /// Performs signed-magnitude addition of two signed big integers and returns the resulting sign.
    /// </summary>
    /// <param name="leftOperand">The magnitude of the left operand.</param>
    /// <param name="leftNegative"><see langword="true"/> if the left operand is negative; otherwise, <see langword="false"/>.</param>
    /// <param name="rightOperand">The magnitude of the right operand.</param>
    /// <param name="rightNegative"><see langword="true"/> if the right operand is negative; otherwise, <see langword="false"/>.</param>
    /// <param name="resultBuffer">The destination buffer receiving the resulting magnitude.</param>
    /// <returns>
    /// <see langword="true"/> if the resulting sum is negative; otherwise, <see langword="false"/>. A zero magnitude must be passed as
    /// non-negative; with that, a zero sum is never negative, so operands of equal magnitude and opposite signs return
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than the wider operand; nothing
    /// is written. A buffer one word wider than the wider operand always holds the sum.</exception>
    public static bool AddSignedMagnitude(
        scoped GpuBigUIntView leftOperand,
        bool leftNegative,
        scoped GpuBigUIntView rightOperand,
        bool rightNegative,
        scoped GpuBigUIntView resultBuffer
    ) =>
        AddSignedMagnitudeCore(
            leftOperand.AsBorrowedBigUInt(),
            leftNegative,
            rightOperand.AsBorrowedBigUInt(),
            rightNegative,
            resultBuffer.AsBorrowedBigUInt()
        );

    // AddSignedMagnitude on the owned type; the public method above takes views and borrows them
    private static bool AddSignedMagnitudeCore(GpuBigUInt leftOperand, bool leftNegative,
        GpuBigUInt rightOperand, bool rightNegative, GpuBigUInt resultBuffer)
    {
        ResultBuffers.ThrowIfNarrower(resultBuffer, Math.Max(leftOperand.NumWords, rightOperand.NumWords),
            nameof(AddSignedMagnitude));
        if (leftNegative == rightNegative ||
            (!DeviceRanges.Overlaps(resultBuffer.Handle, leftOperand.Handle) &&
             !DeviceRanges.Overlaps(resultBuffer.Handle, rightOperand.Handle)))
        {
            return BigIntWideOps.AddSigned(leftOperand, leftNegative, rightOperand, rightNegative, resultBuffer);
        }

        // The difference kernel must not write an operand: a result in place goes through a temporary
        using var staging = resultBuffer.Workspace.RentTemp(resultBuffer.NumWords);
        var negative = BigIntWideOps.AddSigned(leftOperand, leftNegative, rightOperand, rightNegative, staging);
        resultBuffer.CopyFrom(staging);
        return negative;
    }

    /// <summary>
    /// Computes the arbitrary-precision difference of two big integers on the GPU (<c>resultBuffer = leftOperand - rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The minuend operand.</param>
    /// <param name="rightOperand">The subtrahend operand.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the difference.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than <paramref
    /// name="leftOperand"/>; nothing is written. A difference below zero is a domain error.</exception>
    /// <remarks><paramref name="resultBuffer"/> may be an operand or overlap one; the result is the one of a separate
    /// buffer.</remarks>
    public static void Sub(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView resultBuffer
    ) =>
        SubCore(leftOperand.AsBorrowedBigUInt(), rightOperand.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt());

    // Sub on the owned type; the public method above takes views and borrows them
    private static void SubCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ResultBuffers.ThrowIfNarrower(resultBuffer, leftOperand.NumWords, nameof(Sub));
        BigIntWideOps.AddSubtract(leftOperand, rightOperand, resultBuffer, subtract: true);
    }

    /// <summary>
    /// Subtracts a scalar 32-bit unsigned integer from a big integer on the GPU (<c>resultBuffer = leftOperand - rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The minuend operand.</param>
    /// <param name="rightOperand">The 32-bit unsigned scalar subtrahend.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the difference.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> has fewer words than
    /// <paramref name="leftOperand"/>; nothing is written.</exception>
    /// <exception cref="OverflowException">CPU fallback mode only: the difference is below zero.</exception>
    /// <remarks>
    /// <para>
    /// <b>Result width:</b> <paramref name="resultBuffer"/> must have at least as many words as
    /// <paramref name="leftOperand"/>. A wider buffer receives the exact difference and zeros above it. A buffer as
    /// wide as the operand is the checked form: a difference below zero is a domain error, never dropped.
    /// </para>
    /// <para>
    /// <b>Domain errors:</b> on the GPU, reported as <see cref="OverflowException"/> at the next read of the workspace
    /// stream (or by <see cref="GpuWorkspace.ThrowIfDeviceErrors"/>); in CPU fallback mode, thrown by this call. The
    /// contents of <paramref name="resultBuffer"/> are then unspecified.
    /// </para>
    /// <para>
    /// <paramref name="resultBuffer"/> may be <paramref name="leftOperand"/> itself (in place) or overlap it in any
    /// other way; the result is the one of a separate buffer.
    /// </para>
    /// </remarks>
    public static void Sub(scoped GpuBigUIntView leftOperand, uint rightOperand, scoped GpuBigUIntView resultBuffer) =>
        SubCore(leftOperand.AsBorrowedBigUInt(), rightOperand, resultBuffer.AsBorrowedBigUInt());

    // Sub on the owned type; the public method above takes views and borrows them
    private static void SubCore(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer) =>
        WriteElementwise(leftOperand, resultBuffer, rightOperand, static (scalar, operand, result) =>
            operand.Workspace.Provider.Sub(operand.Workspace, operand, scalar, result));

    /// <summary>
    /// Computes the full arbitrary-precision product of two big integers on the GPU (<c>resultBuffer = leftOperand * rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The multiplicand operand.</param>
    /// <param name="rightOperand">The multiplier operand.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the product.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> has fewer than <c>leftOperand.NumWords +
    /// rightOperand.NumWords</c> words; nothing is written.</exception>
    /// <remarks>
    /// <para><b>Buffer Requirements:</b> The destination buffer must have at least <c>leftOperand.NumWords +
    /// rightOperand.NumWords</c> words; the words above the product are cleared.</para>
    /// </remarks>
    public static void Mul(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView resultBuffer
    ) =>
        MulCore(leftOperand.AsBorrowedBigUInt(), rightOperand.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt());

    // Mul on the owned type; the public method above takes views and borrows them
    private static void MulCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var productWords = leftOperand.NumWords + rightOperand.NumWords;
        ResultBuffers.ThrowIfNarrower(resultBuffer, productWords, nameof(Mul));
        leftOperand.Workspace.Provider.Mul(leftOperand.Workspace, leftOperand, rightOperand,
            ResultBuffers.Head(resultBuffer, productWords));
        ResultBuffers.ClearAbove(resultBuffer, productWords);
    }

    /// <summary>
    /// Multiplies a big integer by a scalar 32-bit unsigned integer on the GPU (<c>resultBuffer = leftOperand * rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The multiplicand operand.</param>
    /// <param name="rightOperand">The 32-bit unsigned scalar multiplier.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the product. One word more
    /// than <paramref name="leftOperand"/> always holds it.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> has fewer words than
    /// <paramref name="leftOperand"/>; nothing is written.</exception>
    /// <exception cref="OverflowException">CPU fallback mode only: the product does not fit in a
    /// <paramref name="resultBuffer"/> as wide as <paramref name="leftOperand"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>Result width:</b> <paramref name="resultBuffer"/> must have at least as many words as
    /// <paramref name="leftOperand"/>. A wider buffer receives the exact product and zeros above it. A buffer as
    /// wide as the operand is the checked form: a product that needs another word is a domain error, never dropped.
    /// </para>
    /// <para>
    /// <b>Domain errors:</b> on the GPU, reported as <see cref="OverflowException"/> at the next read of the workspace
    /// stream (or by <see cref="GpuWorkspace.ThrowIfDeviceErrors"/>); in CPU fallback mode, thrown by this call. The
    /// contents of <paramref name="resultBuffer"/> are then unspecified.
    /// </para>
    /// <para>
    /// <paramref name="resultBuffer"/> may be <paramref name="leftOperand"/> itself (in place) or overlap it in any
    /// other way; the result is the one of a separate buffer.
    /// </para>
    /// </remarks>
    public static void Mul(scoped GpuBigUIntView leftOperand, uint rightOperand, scoped GpuBigUIntView resultBuffer) =>
        MulCore(leftOperand.AsBorrowedBigUInt(), rightOperand, resultBuffer.AsBorrowedBigUInt());

    // Mul on the owned type; the public method above takes views and borrows them
    private static void MulCore(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer) =>
        WriteElementwise(leftOperand, resultBuffer, rightOperand, static (scalar, operand, result) =>
            operand.Workspace.Provider.Mul(operand.Workspace, operand, scalar, result));

    /// <summary>
    /// Computes the full arbitrary-precision product of two big integers on the GPU using the Number Theoretic Transform (NTT) algorithm
    /// (<c>resultBuffer = leftOperand * rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The multiplicand operand.</param>
    /// <param name="rightOperand">The multiplier operand.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the product.</param>
    /// <exception cref="ArgumentException">Thrown when <c>resultBuffer.NumWords</c> is less than <c>leftOperand.NumWords + rightOperand.NumWords</c>.</exception>
    public static void MultiplyNtt(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView resultBuffer
    ) =>
        MultiplyNttCore(
            leftOperand.AsBorrowedBigUInt(),
            rightOperand.AsBorrowedBigUInt(),
            resultBuffer.AsBorrowedBigUInt()
        );

    // MultiplyNtt on the owned type; the public method above takes views and borrows them
    private static void MultiplyNttCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer)
    {
        var requiredWords = leftOperand.NumWords + rightOperand.NumWords;
        if (resultBuffer.NumWords < requiredWords)
        {
            throw new ArgumentException(
                $"Result buffer must have at least the sum of input buffer sizes for MultiplyNtt. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }

        if (resultBuffer.NumWords <= 512 && !GpuKernelLoader.IsCpuFallbackMode)
        {
            GpuBigIntMulKernels.LaunchMultiplyNttKernelPtr(
                resultBuffer.ToProxy(), resultBuffer.NumWords,
                leftOperand.ToProxy(), leftOperand.NumWords,
                rightOperand.ToProxy(), rightOperand.NumWords,
                gridDimX: 1, blockDimX: 256, hStream: leftOperand.Workspace.Stream);
            return;
        }

        leftOperand.Workspace.Provider.Mul(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>
    /// Divides an unsigned big integer by a 32-bit unsigned scalar on the GPU (<c>quotientBuffer = dividend / divisor</c>) and returns the 32-bit remainder.
    /// </summary>
    /// <param name="dividend">The dividend operand.</param>
    /// <param name="divisor">The 32-bit unsigned scalar divisor.</param>
    /// <param name="quotientBuffer">The destination buffer in GPU device memory receiving the quotient.</param>
    /// <returns>The 32-bit unsigned remainder (<c>dividend % divisor</c>).</returns>
    /// <exception cref="ArgumentException"><paramref name="quotientBuffer"/> is narrower than <paramref
    /// name="dividend"/>; nothing is written.</exception>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="divisor"/> is zero.</exception>
    public static uint DivideByUInt32(
        scoped GpuBigUIntView dividend,
        uint divisor,
        scoped GpuBigUIntView quotientBuffer
    ) =>
        DivideByUInt32Core(dividend.AsBorrowedBigUInt(), divisor, quotientBuffer.AsBorrowedBigUInt());

    // DivideByUInt32 on the owned type; the public method above takes views and borrows them
    private static uint DivideByUInt32Core(GpuBigUInt dividend, uint divisor, GpuBigUInt quotientBuffer)
    {
        var words = dividend.NumWords;
        ResultBuffers.ThrowIfNarrower(quotientBuffer, words, nameof(DivideByUInt32));
        var workspace = dividend.Workspace;
        uint remainder;

        // A quotient buffer that overlaps the dividend gets the quotient through a temporary, as for separate buffers;
        // the words above the quotient are cleared after the division, so a failed one leaves the buffer as it was
        if (BigIntWideOps.PartlyOverlaps(quotientBuffer, dividend))
        {
            using var staging = workspace.RentTemp(words);
            remainder = workspace.Provider.DivideByUInt32(workspace, dividend, divisor, staging);
            ResultBuffers.Head(quotientBuffer, words).CopyFrom(staging);
        }
        else
        {
            remainder = workspace.Provider.DivideByUInt32(workspace, dividend, divisor,
                ResultBuffers.Head(quotientBuffer, words));
        }

        ResultBuffers.ClearAbove(quotientBuffer, words);
        return remainder;
    }

    /// <summary>
    /// Computes the Greatest Common Divisor (GCD) of two big integers on the GPU using Lehmer's / Euclidean algorithm.
    /// </summary>
    /// <param name="leftOperand">The first big integer.</param>
    /// <param name="rightOperand">The second big integer.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the GCD.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than the narrower operand;
    /// nothing is written.</exception>
    public static void Gcd(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView resultBuffer
    ) =>
        GcdCore(leftOperand.AsBorrowedBigUInt(), rightOperand.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt());

    // Gcd on the owned type; the public method above takes views and borrows them
    private static void GcdCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var words = Math.Max(leftOperand.NumWords, rightOperand.NumWords);
        ResultBuffers.ThrowIfNarrower(resultBuffer, Math.Min(leftOperand.NumWords, rightOperand.NumWords), nameof(Gcd));

        // The kernels take equal widths: the narrower operand is widened, and the gcd, at most the narrower operand,
        // is computed in the wider width and copied into the result buffer when that is narrower
        var workspace = leftOperand.Workspace;
        using GpuBigUInt? leftWide = leftOperand.NumWords == words ? null : leftOperand.PadTo(words);
        using GpuBigUInt? rightWide = rightOperand.NumWords == words ? null : rightOperand.PadTo(words);
        using GpuBigUInt? gcdWide = resultBuffer.NumWords >= words ? null : workspace.RentTemp(words);
        var target = gcdWide ?? ResultBuffers.Head(resultBuffer, words);
        workspace.Provider.Gcd(workspace, leftWide ?? leftOperand, rightWide ?? rightOperand, target);
        if (gcdWide is { } gcd)
        {
            resultBuffer.CopyFrom(gcd, numWords: resultBuffer.NumWords);
        }
        else
        {
            ResultBuffers.ClearAbove(resultBuffer, words);
        }
    }

    /// <summary>
    /// Computes the modular multiplicative inverse such that $((\text{value} \times \text{resultBuffer}) \bmod \text{modulus}) = 1$.
    /// </summary>
    /// <param name="value">The value to invert.</param>
    /// <param name="modulus">The modulus coprime to <paramref name="value"/>.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the modular inverse.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than <paramref name="modulus"/>;
    /// nothing is written.</exception>
    /// <exception cref="ArithmeticException">Thrown when the modular inverse does not exist ($\gcd(\text{value}, \text{modulus}) \neq 1$).</exception>
    /// <exception cref="InvalidOperationException">Thrown when the GPU binary GCD did not converge within its iteration limit (an internal error).</exception>
    public static void ModInverse(
        scoped GpuBigUIntView value,
        scoped GpuBigUIntView modulus,
        scoped GpuBigUIntView resultBuffer
    ) =>
        ModInverseCore(value.AsBorrowedBigUInt(), modulus.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt());

    // ModInverse on the owned type; the public method above takes views and borrows them
    private static void ModInverseCore(GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer)
    {
        var words = modulus.NumWords;
        ResultBuffers.ThrowIfNarrower(resultBuffer, words, nameof(ModInverse));

        // The kernels take the modulus width: a narrower value is widened, a wider one is reduced first (the inverse of
        // v is the inverse of v mod m)
        var workspace = value.Workspace;
        GpuBigUInt? reduced = null;
        try
        {
            if (value.NumWords < words)
            {
                reduced = value.PadTo(words);
            }
            else if (value.NumWords > words)
            {
                reduced = new GpuBigUInt(workspace, words);
                GpuBigIntDivRem.Mod(value, modulus, reduced.Value);
            }

            workspace.Provider.ModInverse(workspace, reduced ?? value, modulus,
                ResultBuffers.Head(resultBuffer, words));
            ResultBuffers.ClearAbove(resultBuffer, words);
        }
        finally
        {
            reduced?.Dispose();
        }
    }

    /// <summary>
    /// Shifts the big integer left by the specified bit count on the GPU (<c>resultBuffer = leftOperand &lt;&lt; shiftBits</c>).
    /// </summary>
    /// <param name="leftOperand">The big integer to shift.</param>
    /// <param name="shiftBits">The number of bit positions to shift left; a negative count shifts right, as
    /// <see cref="BigInteger"/>. A set bit shifted out of a result narrower than the shifted value is a domain
    /// error.</param>
    /// <param name="resultBuffer">The destination buffer receiving the shifted value.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than <paramref
    /// name="leftOperand"/>; nothing is written. A buffer narrower than the shifted value is the checked form: a
    /// shifted-out bit is a domain error.</exception>
    /// <remarks><paramref name="resultBuffer"/> may be <paramref name="leftOperand"/> itself or overlap it; the result
    /// is the one of a separate buffer.</remarks>
    public static void ShiftLeft(
        scoped GpuBigUIntView leftOperand,
        int shiftBits,
        scoped GpuBigUIntView resultBuffer
    ) =>
        ShiftLeftCore(leftOperand.AsBorrowedBigUInt(), shiftBits, resultBuffer.AsBorrowedBigUInt());

    /// <summary>
    /// <c>resultBuffer = leftOperand &lt;&lt; shiftBits</c> in the operand's width without the check of the bits
    /// shifted out, for internal callers whose shift provably drops only zero bits (a normalization by the leading
    /// zeros, or a value whose bit length the caller bounded). Each call site states why its bits fit.
    /// </summary>
    /// <param name="leftOperand">The operand.</param>
    /// <param name="shiftBits">The shift, at least 0.</param>
    /// <param name="resultBuffer">The result, as wide as <paramref name="leftOperand"/>.</param>
    internal static void ShiftLeftUnchecked(GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer)
    {
        if (shiftBits < 0 || resultBuffer.NumWords != leftOperand.NumWords)
        {
            throw new ArgumentException(
                $"An unchecked shift takes a non-negative count and equal widths. shift: {shiftBits}, a: " +
                $"{leftOperand.NumWords}, result: {resultBuffer.NumWords} words.", nameof(resultBuffer));
        }

#if DEBUG
        // Debug builds check each caller's claim: a set bit shifted out is reported as a domain error at the next read
        BigIntWideOps.CheckFitsBits(leftOperand,
            Math.Max(0L, (long)BigIntWidths.BitsPerWord * leftOperand.NumWords - shiftBits));
#endif
        leftOperand.Workspace.Provider.ShiftLeft(leftOperand.Workspace, leftOperand, shiftBits, resultBuffer);
    }

    // ShiftLeft on the owned type; the public method above takes views and borrows them
    private static void ShiftLeftCore(GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer)
    {
        if (shiftBits < 0)
        {
            // A right shift by -shiftBits; -int.MinValue does not fit in an int, and the shift moves every bit out
            if (shiftBits == int.MinValue)
            {
                resultBuffer.Workspace.Provider.ClearMemoryAsync(resultBuffer.DevicePointer, resultBuffer.NumWords,
                    resultBuffer.Workspace.Stream);
                return;
            }

            ShiftRightCore(leftOperand, -shiftBits, resultBuffer);
            return;
        }

        ResultBuffers.ThrowIfNarrower(resultBuffer, leftOperand.NumWords, nameof(ShiftLeft));

        // Below the bound (32 a + k bits) the call is the checked form: a bit of the source shifted past the result is
        // a domain error. Checked before the shift, which may write the source in place.
        var keptBits = (long)BigIntWidths.BitsPerWord * resultBuffer.NumWords - shiftBits;
        if (keptBits < (long)BigIntWidths.BitsPerWord * leftOperand.NumWords)
        {
            BigIntWideOps.CheckFitsBits(leftOperand, Math.Max(0L, keptBits));
        }

        if (resultBuffer.NumWords == leftOperand.NumWords)
        {
            leftOperand.Workspace.Provider.ShiftLeft(leftOperand.Workspace, leftOperand, shiftBits, resultBuffer);
            return;
        }

        ShiftAndResizeCore(leftOperand, shiftBits, resultBuffer);
    }

    /// <summary>
    /// Shifts the big integer right by the specified bit count on the GPU (<c>resultBuffer = leftOperand &gt;&gt; shiftBits</c>).
    /// </summary>
    /// <param name="leftOperand">The big integer to shift.</param>
    /// <param name="shiftBits">The number of bit positions to shift right; a negative count shifts left, as
    /// <see cref="BigInteger"/>. Bits shifted below the lowest word are dropped (division by a power of two,
    /// rounded down); a negative count is checked as <see cref="ShiftLeft"/>.</param>
    /// <param name="resultBuffer">The destination buffer receiving the shifted value.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than the bits that remain (<c>32
    /// * leftOperand.NumWords - shiftBits</c>, at least one word), or <paramref name="shiftBits"/> is <see
    /// cref="int.MinValue"/>.</exception>
    /// <remarks><paramref name="resultBuffer"/> may be <paramref name="leftOperand"/> itself or overlap it; the result
    /// is the one of a separate buffer.</remarks>
    public static void ShiftRight(
        scoped GpuBigUIntView leftOperand,
        int shiftBits,
        scoped GpuBigUIntView resultBuffer
    ) =>
        ShiftRightCore(leftOperand.AsBorrowedBigUInt(), shiftBits, resultBuffer.AsBorrowedBigUInt());

    // ShiftRight on the owned type; the public method above takes views and borrows them
    private static void ShiftRightCore(GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer)
    {
        if (shiftBits < 0)
        {
            // A left shift by -shiftBits; int.MinValue is 2^31 bits, which no buffer holds
            if (shiftBits == int.MinValue)
            {
                throw new ArgumentOutOfRangeException(nameof(shiftBits),
                    "A shift right by int.MinValue is a shift left by 2^31 bits, which no buffer holds.");
            }

            ShiftLeftCore(leftOperand, -shiftBits, resultBuffer);
            return;
        }

        // The result has at most 32 a - k bits, at least one word
        var bound = Math.Max((long)BigIntWidths.BitsPerWord * leftOperand.NumWords - shiftBits, 1L);
        ResultBuffers.ThrowIfNarrower(resultBuffer, BigIntWidths.Words(bound), nameof(ShiftRight));
        if (resultBuffer.NumWords == leftOperand.NumWords)
        {
            leftOperand.Workspace.Provider.ShiftRight(leftOperand.Workspace, leftOperand, shiftBits, resultBuffer);
            return;
        }

        ShiftAndResizeCore(leftOperand, -shiftBits, resultBuffer);
    }

    /// <summary>
    /// Shifts <paramref name="source"/> by a signed bit offset (left if positive, right if negative) and writes the resized result into <paramref name="resultBuffer"/>.
    /// </summary>
    /// <param name="source">The source unsigned integer buffer of arbitrary word length.</param>
    /// <param name="signedBitShift">The signed bit shift (positive for left shift, negative for right shift).</param>
    /// <param name="resultBuffer">The destination buffer of arbitrary word length.</param>
    public static void ShiftAndResize(
        scoped GpuBigUIntView source,
        int signedBitShift,
        scoped GpuBigUIntView resultBuffer
    ) =>
        ShiftAndResizeCore(source.AsBorrowedBigUInt(), signedBitShift, resultBuffer.AsBorrowedBigUInt());

    // ShiftAndResize on the owned type; the public method above takes views and borrows them
    private static void ShiftAndResizeCore(GpuBigUInt source, int signedBitShift, GpuBigUInt resultBuffer)
    {
        var workspace = source.Workspace;
        if (!GpuKernelLoader.IsCpuFallbackMode)
        {
            var wordShift = signedBitShift / 32;
            var bitShift = signedBitShift % 32;
            if (bitShift < 0)
            {
                wordShift--;
                bitShift += 32;
            }

            var blockDim = (uint)Math.Min(1024, Math.Max(32, (resultBuffer.NumWords + 31) / 32 * 32));
            if (DeviceRanges.Overlaps(source.Handle, resultBuffer.Handle))
            {
                using var tempCopy = workspace.RentTemp(source.NumWords);
                workspace.Provider.CopyDeviceToDevice(tempCopy.DevicePointer, source.DevicePointer, source.NumWords, workspace.Stream);
                GpuBigFloatKernels.LaunchFloatNormalizeKernelPtr(
                    tempCopy.ToProxy(), source.NumWords,
                    resultBuffer.ToProxy(), resultBuffer.NumWords,
                    wordShift, bitShift,
                    gridDimX: 1, blockDimX: blockDim, hStream: workspace.Stream);
            }
            else
            {
                GpuBigFloatKernels.LaunchFloatNormalizeKernelPtr(
                    source.ToProxy(), source.NumWords,
                    resultBuffer.ToProxy(), resultBuffer.NumWords,
                    wordShift, bitShift,
                    gridDimX: 1, blockDimX: blockDim, hStream: workspace.Stream);
            }

            return;
        }

        var srcWords = ArrayPool<uint>.Shared.Rent(source.NumWords);
        var dstWords = ArrayPool<uint>.Shared.Rent(resultBuffer.NumWords);
        try
        {
            workspace.Provider.CopyDeviceToHost(srcWords, source.DevicePointer, source.NumWords, workspace.Stream);
            var val = BigIntExtensions.ToBigInteger(srcWords.AsSpan(0, source.NumWords));
            // -int.MinValue does not fit in an int; shifting by it moves every bit out
            var shifted = signedBitShift >= 0 ? val << signedBitShift
                : signedBitShift == int.MinValue ? BigInteger.Zero : val >> -signedBitShift;
            shifted.WriteLimbs(dstWords.AsSpan(0, resultBuffer.NumWords));
            workspace.Provider.CopyHostToDevice(resultBuffer.DevicePointer, dstWords, resultBuffer.NumWords, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(srcWords);
            ArrayPool<uint>.Shared.Return(dstWords);
        }
    }

    /// <summary>
    /// Normalizes <paramref name="source"/> so that its most significant non-zero bit is aligned to the MSB of <paramref name="resultBuffer"/>,
    /// returning the signed bit shift applied (<c>resultBuffer.NumWords * 32 - actualBits</c>).
    /// </summary>
    /// <param name="source">The source unsigned integer buffer.</param>
    /// <param name="resultBuffer">The destination buffer receiving the MSB-aligned limbs.</param>
    /// <param name="actualBits">When this method returns, contains the bit length of <paramref name="source"/> prior to normalization.</param>
    /// <returns>The signed bit shift applied (or <c>0</c> if <paramref name="source"/> is zero).</returns>
    public static int NormalizeToMsb(
        scoped GpuBigUIntView source,
        scoped GpuBigUIntView resultBuffer,
        out int actualBits
    ) =>
        NormalizeToMsbCore(source.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt(), out actualBits);

    // NormalizeToMsb on the owned type; the public method above takes views and borrows them
    private static int NormalizeToMsbCore(GpuBigUInt source, GpuBigUInt resultBuffer, out int actualBits)
    {
        var workspace = source.Workspace;
        actualBits = source.GetBitLength();
        if (actualBits == 0)
        {
            workspace.Provider.ClearMemoryAsync(resultBuffer.DevicePointer, resultBuffer.NumWords, workspace.Stream);
            return 0;
        }

        var targetBits = resultBuffer.NumWords * 32;
        var shift = targetBits - actualBits;
        ShiftAndResize(source, shift, resultBuffer);
        return shift;
    }

    /// <summary>
    /// Performs bitwise AND (<c>resultBuffer = leftOperand &amp; rightOperand</c>) on the GPU.
    /// </summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The destination buffer receiving the bitwise AND result.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than the narrower operand;
    /// nothing is written.</exception>
    /// <remarks><paramref name="resultBuffer"/> may be an operand or overlap one; the result is the one of a separate
    /// buffer.</remarks>
    public static void BitwiseAnd(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView resultBuffer
    ) =>
        BitwiseAndCore(
            leftOperand.AsBorrowedBigUInt(),
            rightOperand.AsBorrowedBigUInt(),
            resultBuffer.AsBorrowedBigUInt()
        );

    // BitwiseAnd on the owned type; the public method above takes views and borrows them
    private static void BitwiseAndCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ResultBuffers.ThrowIfNarrower(resultBuffer, Math.Min(leftOperand.NumWords, rightOperand.NumWords),
            nameof(BitwiseAnd));
        BigIntWideOps.Bitwise(leftOperand, rightOperand, resultBuffer, GpuBigIntWideKernels.BitwiseAnd);
    }

    /// <summary>
    /// Performs bitwise OR (<c>resultBuffer = leftOperand | rightOperand</c>) on the GPU.
    /// </summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The destination buffer receiving the bitwise OR result.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than the wider operand; nothing
    /// is written.</exception>
    /// <remarks><paramref name="resultBuffer"/> may be an operand or overlap one; the result is the one of a separate
    /// buffer.</remarks>
    public static void BitwiseOr(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView resultBuffer
    ) =>
        BitwiseOrCore(
            leftOperand.AsBorrowedBigUInt(),
            rightOperand.AsBorrowedBigUInt(),
            resultBuffer.AsBorrowedBigUInt()
        );

    // BitwiseOr on the owned type; the public method above takes views and borrows them
    private static void BitwiseOrCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ResultBuffers.ThrowIfNarrower(resultBuffer, Math.Max(leftOperand.NumWords, rightOperand.NumWords),
            nameof(BitwiseOr));
        BigIntWideOps.Bitwise(leftOperand, rightOperand, resultBuffer, GpuBigIntWideKernels.BitwiseOr);
    }

    /// <summary>
    /// Performs bitwise XOR (<c>resultBuffer = leftOperand ^ rightOperand</c>) on the GPU.
    /// </summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The destination buffer receiving the bitwise XOR result.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than the wider operand; nothing
    /// is written.</exception>
    /// <remarks><paramref name="resultBuffer"/> may be an operand or overlap one; the result is the one of a separate
    /// buffer.</remarks>
    public static void BitwiseXor(
        scoped GpuBigUIntView leftOperand,
        scoped GpuBigUIntView rightOperand,
        scoped GpuBigUIntView resultBuffer
    ) =>
        BitwiseXorCore(
            leftOperand.AsBorrowedBigUInt(),
            rightOperand.AsBorrowedBigUInt(),
            resultBuffer.AsBorrowedBigUInt()
        );

    // BitwiseXor on the owned type; the public method above takes views and borrows them
    private static void BitwiseXorCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ResultBuffers.ThrowIfNarrower(resultBuffer, Math.Max(leftOperand.NumWords, rightOperand.NumWords),
            nameof(BitwiseXor));
        BigIntWideOps.Bitwise(leftOperand, rightOperand, resultBuffer, GpuBigIntWideKernels.BitwiseXor);
    }

    /// <summary>
    /// Performs bitwise NOT (one's complement) (<c>resultBuffer = ~leftOperand</c>) on the GPU.
    /// </summary>
    /// <param name="leftOperand">The operand to invert.</param>
    /// <param name="resultBuffer">The destination buffer receiving the bitwise NOT result.</param>
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    /// <remarks><paramref name="resultBuffer"/> may be an operand or overlap one; the result is the one of a separate
    /// buffer.</remarks>
    public static void BitwiseNot(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView resultBuffer) =>
        BitwiseNotCore(leftOperand.AsBorrowedBigUInt(), resultBuffer.AsBorrowedBigUInt());

    // BitwiseNot on the owned type; the public method above takes views and borrows them
    private static void BitwiseNotCore(GpuBigUInt leftOperand, GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        WriteElementwise(leftOperand, resultBuffer, 0, static (_, operand, result) =>
            operand.Workspace.Provider.BitwiseNot(operand.Workspace, operand, result));
    }

    // Runs an element-wise operation of operand into resultBuffer, or into a temporary copied into resultBuffer when
    // the two partly overlap (BigIntWideOps.PartlyOverlaps)
    private static void WriteElementwise<TState>(GpuBigUInt operand, GpuBigUInt resultBuffer, TState state,
        Action<TState, GpuBigUInt, GpuBigUInt> write)
    {
        if (!BigIntWideOps.PartlyOverlaps(resultBuffer, operand))
        {
            write(state, operand, resultBuffer);
            return;
        }

        using var staging = operand.Workspace.RentTemp(resultBuffer.NumWords);
        write(state, operand, staging);
        resultBuffer.CopyFrom(staging);
    }

    /// <summary>
    /// Determines whether <paramref name="leftOperand"/> is strictly less than <paramref name="rightOperand"/> on the GPU.
    /// </summary>
    /// <param name="leftOperand">The first value to compare.</param>
    /// <param name="rightOperand">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="leftOperand"/> is strictly less than <paramref name="rightOperand"/>; otherwise, <see langword="false"/>.</returns>
    public static bool IsLessThan(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand) =>
        IsLessThanCore(leftOperand.AsBorrowedBigUInt(), rightOperand.AsBorrowedBigUInt());

    // IsLessThan on the owned type; the public method above takes views and borrows them
    private static bool IsLessThanCore(GpuBigUInt leftOperand, GpuBigUInt rightOperand)
    {
        // Operands of any widths: the compare reads a missing word as zero
        return leftOperand.CompareTo(rightOperand) < 0;
    }

    /// <summary>
    /// Computes $10^{\text{exponent}}$ on the GPU using binary exponentiation by squaring.
    /// </summary>
    /// <param name="workspace">The GPU execution workspace.</param>
    /// <param name="exponent">The non-negative power of 10 to compute.</param>
    /// <returns>A new <see cref="GpuBigUInt"/> containing the result $10^{\text{exponent}}$.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="exponent"/> is negative or exceeds 50,000,000.</exception>
    public static GpuBigUInt Pow10(GpuWorkspace workspace, int exponent)
    {
        const int MaxPow10Exponent = 50_000_000;
        if (exponent < 0 || exponent > MaxPow10Exponent)
        {
            throw new ArgumentOutOfRangeException(nameof(exponent),
                $"Exponent must be between 0 and {MaxPow10Exponent}.");
        }

        if (exponent <= 9)
        {
            return GpuBigUInt.FromBigInteger(workspace, BigInteger.Pow(10, exponent));
        }

        var resultGpu = GpuBigUInt.FromBigInteger(workspace, 1);
        GpuBigUInt? currentGpu = null;
        var success = false;
        try
        {
            currentGpu = GpuBigUInt.FromBigInteger(workspace, 10);
            var exp = exponent;

            while (exp > 0)
            {
                if ((exp & 1) == 1)
                {
                    var nextResult = MulAndTrim(resultGpu, currentGpu.Value);
                    resultGpu.Dispose();
                    resultGpu = nextResult;
                }

                exp >>= 1;
                if (exp > 0)
                {
                    var nextCurrent = MulAndTrim(currentGpu.Value, currentGpu.Value);
                    currentGpu.Value.Dispose();
                    currentGpu = nextCurrent;
                }
            }

            success = true;
            return resultGpu;
        }
        finally
        {
            if (currentGpu.HasValue)
            {
                currentGpu.Value.Dispose();
            }

            if (!success)
            {
                resultGpu.Dispose();
            }
        }
    }

    // [WHY] Dynamically trims trailing zero limbs after multiplication to prevent memory explosion during large power computations.
    private static GpuBigUInt MulAndTrim(GpuBigUInt left, GpuBigUInt right)
    {
        var workspace = left.Workspace;
        var nextBuffer = new GpuBigUInt(workspace, left.NumWords + right.NumWords);
        GpuBigUInt? trimmed = null;
        try
        {
            Mul(left, right, nextBuffer);
            var actualBits = GpuBigIntDivRem.GetActualBits(nextBuffer);
            var actualWords = (actualBits + 31) / 32;
            if (actualWords == 0)
            {
                actualWords = 1;
            }

            // The value has actualWords words, so the low words hold it (PadTo only widens)
            trimmed = nextBuffer.CopyToWidth(actualWords);

            var result = trimmed;
            trimmed = null; // Ownership transferred to caller
            return result.Value;
        }
        finally
        {
            nextBuffer.Dispose();
            trimmed?.Dispose();
        }
    }
}


