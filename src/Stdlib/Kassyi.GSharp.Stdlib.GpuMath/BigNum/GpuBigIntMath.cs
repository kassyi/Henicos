namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides static mathematical and bitwise operations for <see cref="GpuBigUInt"/> and arbitrary-precision integer calculations on the GPU.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Dispatches high-throughput parallel compute kernels via <see cref="GpuWorkspace.Provider"/>.</para>
/// <para><b>Memory Alignment:</b> Unless specified otherwise, binary arithmetic operations require operand and result buffers to have identical word lengths (<see cref="GpuBigUInt.NumWords"/>).</para>
/// <para><b>Thread Safety:</b> Static methods are thread-safe assuming distinct device memory buffers are supplied for concurrent operations.</para>
/// </remarks>
public static class GpuBigIntMath
{
    // [WHY] Validates buffer lengths at the host boundary before GPU kernel dispatch to avoid silent CUDA out-of-bounds writes.
    [ExcludeFromCodeCoverage] private static void ThrowIfSizeMismatch(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        if (leftOperand.NumWords != rightOperand.NumWords || leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

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
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    public static void Add(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.Add(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>
    /// Adds a scalar 32-bit unsigned integer to a big integer on the GPU (<c>resultBuffer = leftOperand + rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The big integer addend.</param>
    /// <param name="rightOperand">The 32-bit unsigned scalar addend.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the sum.</param>
    public static void Add(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer) =>
        leftOperand.Workspace.Provider.Add(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);

    /// <summary>
    /// Performs signed-magnitude addition of two signed big integers and returns the resulting sign.
    /// </summary>
    /// <param name="leftOperand">The magnitude of the left operand.</param>
    /// <param name="leftNegative"><see langword="true"/> if the left operand is negative; otherwise, <see langword="false"/>.</param>
    /// <param name="rightOperand">The magnitude of the right operand.</param>
    /// <param name="rightNegative"><see langword="true"/> if the right operand is negative; otherwise, <see langword="false"/>.</param>
    /// <param name="resultBuffer">The destination buffer receiving the resulting magnitude.</param>
    /// <returns><see langword="true"/> if the resulting sum is negative; otherwise, <see langword="false"/>.</returns>
    public static bool AddSignedMagnitude(GpuBigUInt leftOperand, bool leftNegative,
        GpuBigUInt rightOperand, bool rightNegative, GpuBigUInt resultBuffer)
    {
        if (leftNegative == rightNegative)
        {
            Add(leftOperand, rightOperand, resultBuffer);
            return leftNegative;
        }

        var comparison = leftOperand.CompareTo(rightOperand);
        if (comparison >= 0)
        {
            Sub(leftOperand, rightOperand, resultBuffer);
            return leftNegative;
        }

        Sub(rightOperand, leftOperand, resultBuffer);
        return rightNegative;
    }

    /// <summary>
    /// Computes the arbitrary-precision difference of two big integers on the GPU (<c>resultBuffer = leftOperand - rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The minuend operand.</param>
    /// <param name="rightOperand">The subtrahend operand.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the difference.</param>
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    public static void Sub(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.Sub(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>
    /// Subtracts a scalar 32-bit unsigned integer from a big integer on the GPU (<c>resultBuffer = leftOperand - rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The minuend operand.</param>
    /// <param name="rightOperand">The 32-bit unsigned scalar subtrahend.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the difference.</param>
    public static void Sub(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer) =>
        leftOperand.Workspace.Provider.Sub(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);

    /// <summary>
    /// Computes the full arbitrary-precision product of two big integers on the GPU (<c>resultBuffer = leftOperand * rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The multiplicand operand.</param>
    /// <param name="rightOperand">The multiplier operand.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the product.</param>
    /// <exception cref="ArgumentException">Thrown when <c>resultBuffer.NumWords</c> is not equal to <c>leftOperand.NumWords + rightOperand.NumWords</c>.</exception>
    /// <remarks>
    /// <para><b>Buffer Requirements:</b> The destination buffer must have a word capacity exactly equal to <c>leftOperand.NumWords + rightOperand.NumWords</c>.</para>
    /// </remarks>
    public static void Mul(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        if (resultBuffer.NumWords != leftOperand.NumWords + rightOperand.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must be the sum of input buffer sizes for Mul. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }

        leftOperand.Workspace.Provider.Mul(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>
    /// Multiplies a big integer by a scalar 32-bit unsigned integer on the GPU (<c>resultBuffer = leftOperand * rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The multiplicand operand.</param>
    /// <param name="rightOperand">The 32-bit unsigned scalar multiplier.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the product.</param>
    public static void Mul(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer) =>
        leftOperand.Workspace.Provider.Mul(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);

    /// <summary>
    /// Computes the full arbitrary-precision product of two big integers on the GPU using the Number Theoretic Transform (NTT) algorithm
    /// (<c>resultBuffer = leftOperand * rightOperand</c>).
    /// </summary>
    /// <param name="leftOperand">The multiplicand operand.</param>
    /// <param name="rightOperand">The multiplier operand.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the product.</param>
    /// <exception cref="ArgumentException">Thrown when <c>resultBuffer.NumWords</c> is less than <c>leftOperand.NumWords + rightOperand.NumWords</c>.</exception>
    public static void MultiplyNtt(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer)
    {
        var requiredWords = leftOperand.NumWords + rightOperand.NumWords;
        if (resultBuffer.NumWords < requiredWords)
        {
            throw new ArgumentException(
                $"Result buffer must have at least the sum of input buffer sizes for MultiplyNtt. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }

        if (resultBuffer.NumWords <= 512 &&
            leftOperand.Workspace.Provider is GSharpKernelProvider &&
            !GpuKernelLoader.IsCpuFallbackMode)
        {
            GpuBigIntMulKernels.LaunchMultiplyNttKernelPtr(
                resultBuffer.DevicePointer, resultBuffer.NumWords,
                leftOperand.DevicePointer, leftOperand.NumWords,
                rightOperand.DevicePointer, rightOperand.NumWords,
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
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="divisor"/> is zero.</exception>
    public static uint DivideByUInt32(GpuBigUInt dividend, uint divisor, GpuBigUInt quotientBuffer)
    {
        ThrowIfSizeMismatch(dividend, quotientBuffer);
        return dividend.Workspace.Provider.DivideByUInt32(dividend.Workspace, dividend, divisor, quotientBuffer);
    }

    /// <summary>
    /// Computes the Greatest Common Divisor (GCD) of two big integers on the GPU using Lehmer's / Euclidean algorithm.
    /// </summary>
    /// <param name="leftOperand">The first big integer.</param>
    /// <param name="rightOperand">The second big integer.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the GCD.</param>
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    public static void Gcd(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.Gcd(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>
    /// Computes the modular multiplicative inverse such that $((\text{value} \times \text{resultBuffer}) \bmod \text{modulus}) = 1$.
    /// </summary>
    /// <param name="value">The value to invert.</param>
    /// <param name="modulus">The modulus coprime to <paramref name="value"/>.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the modular inverse.</param>
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    /// <exception cref="ArithmeticException">Thrown when the modular inverse does not exist ($\gcd(\text{value}, \text{modulus}) \neq 1$).</exception>
    public static void ModInverse(GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(value, modulus, resultBuffer);
        value.Workspace.Provider.ModInverse(value.Workspace, value, modulus, resultBuffer);
    }

    /// <summary>
    /// Shifts the big integer left by the specified bit count on the GPU (<c>resultBuffer = leftOperand &lt;&lt; shiftBits</c>).
    /// </summary>
    /// <param name="leftOperand">The big integer to shift.</param>
    /// <param name="shiftBits">The number of bit positions to shift left.</param>
    /// <param name="resultBuffer">The destination buffer receiving the shifted value.</param>
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    public static void ShiftLeft(GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Workspace.Provider.ShiftLeft(leftOperand.Workspace, leftOperand, shiftBits, resultBuffer);
    }

    /// <summary>
    /// Shifts the big integer right by the specified bit count on the GPU (<c>resultBuffer = leftOperand &gt;&gt; shiftBits</c>).
    /// </summary>
    /// <param name="leftOperand">The big integer to shift.</param>
    /// <param name="shiftBits">The number of bit positions to shift right.</param>
    /// <param name="resultBuffer">The destination buffer receiving the shifted value.</param>
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    public static void ShiftRight(GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Workspace.Provider.ShiftRight(leftOperand.Workspace, leftOperand, shiftBits, resultBuffer);
    }

    /// <summary>
    /// Shifts <paramref name="source"/> by a signed bit offset (left if positive, right if negative) and writes the resized result into <paramref name="resultBuffer"/>.
    /// </summary>
    /// <param name="source">The source unsigned integer buffer of arbitrary word length.</param>
    /// <param name="signedBitShift">The signed bit shift (positive for left shift, negative for right shift).</param>
    /// <param name="resultBuffer">The destination buffer of arbitrary word length.</param>
    public static void ShiftAndResize(GpuBigUInt source, int signedBitShift, GpuBigUInt resultBuffer)
    {
        var workspace = source.Workspace;
        if (workspace.Provider is GSharpKernelProvider && !GpuKernelLoader.IsCpuFallbackMode)
        {
            var wordShift = signedBitShift / 32;
            var bitShift = signedBitShift % 32;
            if (bitShift < 0)
            {
                wordShift--;
                bitShift += 32;
            }

            var blockDim = (uint)Math.Min(1024, Math.Max(32, (resultBuffer.NumWords + 31) / 32 * 32));
            if (source.DevicePointer == resultBuffer.DevicePointer)
            {
                using var tempCopy = workspace.RentTemp(source.NumWords);
                workspace.Provider.CopyDeviceToDevice(tempCopy.DevicePointer, source.DevicePointer, source.NumWords, workspace.Stream);
                GpuBigFloatKernels.LaunchFloatNormalizeKernelPtr(
                    tempCopy.DevicePointer, source.NumWords,
                    resultBuffer.DevicePointer, resultBuffer.NumWords,
                    wordShift, bitShift,
                    gridDimX: 1, blockDimX: blockDim, hStream: workspace.Stream);
            }
            else
            {
                GpuBigFloatKernels.LaunchFloatNormalizeKernelPtr(
                    source.DevicePointer, source.NumWords,
                    resultBuffer.DevicePointer, resultBuffer.NumWords,
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
            var shifted = signedBitShift >= 0 ? (val << signedBitShift) : (val >> -signedBitShift);
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
    public static int NormalizeToMsb(GpuBigUInt source, GpuBigUInt resultBuffer, out int actualBits)
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
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    public static void BitwiseAnd(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.BitwiseAnd(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>
    /// Performs bitwise OR (<c>resultBuffer = leftOperand | rightOperand</c>) on the GPU.
    /// </summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The destination buffer receiving the bitwise OR result.</param>
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    public static void BitwiseOr(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.BitwiseOr(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>
    /// Performs bitwise XOR (<c>resultBuffer = leftOperand ^ rightOperand</c>) on the GPU.
    /// </summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The destination buffer receiving the bitwise XOR result.</param>
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    public static void BitwiseXor(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.BitwiseXor(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>
    /// Performs bitwise NOT (one's complement) (<c>resultBuffer = ~leftOperand</c>) on the GPU.
    /// </summary>
    /// <param name="leftOperand">The operand to invert.</param>
    /// <param name="resultBuffer">The destination buffer receiving the bitwise NOT result.</param>
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    public static void BitwiseNot(GpuBigUInt leftOperand, GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Workspace.Provider.BitwiseNot(leftOperand.Workspace, leftOperand, resultBuffer);
    }

    /// <summary>
    /// Determines whether <paramref name="leftOperand"/> is strictly less than <paramref name="rightOperand"/> on the GPU.
    /// </summary>
    /// <param name="leftOperand">The first value to compare.</param>
    /// <param name="rightOperand">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="leftOperand"/> is strictly less than <paramref name="rightOperand"/>; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when buffer word lengths do not match.</exception>
    public static bool IsLessThan(GpuBigUInt leftOperand, GpuBigUInt rightOperand)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand);
        using var temp = new GpuBigUInt(leftOperand.Workspace, leftOperand.NumWords);
        Sub(leftOperand, rightOperand, temp);
        var topWord = temp.GetTopWords(1)[0];
        return (topWord & 0x80000000u) != 0;
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

            trimmed = nextBuffer.PadTo(actualWords);

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


