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


