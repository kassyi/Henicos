namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Provides static mathematical and bitwise operations for <see cref="GpuUIntBigInteger"/> on the GPU.
/// </summary>
public static class GpuBigIntMath
{
    private static void ThrowIfSizeMismatch(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        if (leftOperand.NumWords != rightOperand.NumWords || leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    private static void ThrowIfSizeMismatch(GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer)
    {
        if (leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must have the same size as input buffer. a: {leftOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    /// <summary>Computes arbitrary-precision addition (<c>leftOperand + rightOperand</c>) on the GPU.</summary>
    public static void Add(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.Add(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Adds a scalar 32-bit unsigned integer to <paramref name="leftOperand"/> on the GPU.</summary>
    public static void Add(GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer) =>
        leftOperand.Workspace.Provider.Add(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);

    /// <summary>
    /// Performs signed-magnitude addition of two values and returns the resulting sign.
    /// </summary>
    public static bool AddSignedMagnitude(GpuUIntBigInteger leftOperand, bool leftNegative,
        GpuUIntBigInteger rightOperand, bool rightNegative, GpuUIntBigInteger resultBuffer)
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

    /// <summary>Computes arbitrary-precision subtraction (<c>leftOperand - rightOperand</c>) on the GPU.</summary>
    public static void Sub(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.Sub(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Subtracts a scalar 32-bit unsigned integer from <paramref name="leftOperand"/> on the GPU.</summary>
    public static void Sub(GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer) =>
        leftOperand.Workspace.Provider.Sub(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);

    /*
     * Note (Out-of-Core Architecture):
     * Monolithic single-kernel GPU multiplication is intentionally avoided to support astronomical, unbounded numbers.
     * To compute numbers beyond physical GPU VRAM limits, multiplication is orchestrated by the host (C# AST and expression
     * tree evaluation) utilizing host memory and disk storage as an external workspace, while leveraging fine-grained GPU
     * kernels (Square, Add, Sub, NTT) as high-throughput building blocks.
     */
    /// <summary>
    /// Computes full arbitrary-precision multiplication (<c>leftOperand * rightOperand</c>) on the GPU.
    /// </summary>
    /// <remarks>The destination buffer must have length equal to <c>leftOperand.NumWords + rightOperand.NumWords</c>.</remarks>
    public static void Mul(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        if (resultBuffer.NumWords != leftOperand.NumWords + rightOperand.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must be the sum of input buffer sizes for Mul. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }

        leftOperand.Workspace.Provider.Mul(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Multiplies <paramref name="leftOperand"/> by a scalar 32-bit unsigned integer on the GPU.</summary>
    public static void Mul(GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer) =>
        leftOperand.Workspace.Provider.Mul(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);

    /// <summary>Computes the Greatest Common Divisor (GCD) of two big integers on the GPU.</summary>
    public static void Gcd(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.Gcd(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Shifts the big integer left by the specified bit count on the GPU.</summary>
    public static void ShiftLeft(GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Workspace.Provider.ShiftLeft(leftOperand.Workspace, leftOperand, shiftBits, resultBuffer);
    }

    /// <summary>Shifts the big integer right by the specified bit count on the GPU.</summary>
    public static void ShiftRight(GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Workspace.Provider.ShiftRight(leftOperand.Workspace, leftOperand, shiftBits, resultBuffer);
    }

    /// <summary>Performs bitwise AND (<c>leftOperand &amp; rightOperand</c>) on GPU and stores result in destination buffer.</summary>
    public static void BitwiseAnd(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.BitwiseAnd(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Performs bitwise OR on the GPU.</summary>
    public static void BitwiseOr(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.BitwiseOr(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Performs bitwise XOR on the GPU.</summary>
    public static void BitwiseXor(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Workspace.Provider.BitwiseXor(leftOperand.Workspace, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Performs bitwise NOT on the GPU.</summary>
    public static void BitwiseNot(GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Workspace.Provider.BitwiseNot(leftOperand.Workspace, leftOperand, resultBuffer);
    }

    /// <summary>Determines whether <paramref name="leftOperand"/> is strictly less than <paramref name="rightOperand"/> on the GPU.</summary>
    public static bool IsLessThan(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand);
        using var temp = new GpuUIntBigInteger(leftOperand.Workspace, leftOperand.NumWords);
        Sub(leftOperand, rightOperand, temp);
        var topWord = temp.GetTopWords(1)[0];
        return (topWord & 0x80000000u) != 0;
    }

    /// <summary>
    /// Computes 10 raised to the specified exponent on the GPU using exponentiation by squaring.
    /// </summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="exponent">The exponent.</param>
    /// <returns>A new <see cref="GpuUIntBigInteger"/> containing the result.</returns>
    public static GpuUIntBigInteger Pow10(GpuWorkspace context, int exponent)
    {
        const int MaxPow10Exponent = 50_000_000;
        if (exponent < 0 || exponent > MaxPow10Exponent)
        {
            throw new ArgumentOutOfRangeException(nameof(exponent),
                $"Exponent must be between 0 and {MaxPow10Exponent}.");
        }

        if (exponent <= 9)
        {
            return GpuUIntBigInteger.FromBigInteger(context, BigInteger.Pow(10, exponent));
        }

        var resultGpu = GpuUIntBigInteger.FromBigInteger(context, 1);
        GpuUIntBigInteger? currentGpu = null;
        var success = false;
        try
        {
            currentGpu = GpuUIntBigInteger.FromBigInteger(context, 10);
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

    private static GpuUIntBigInteger MulAndTrim(GpuUIntBigInteger left, GpuUIntBigInteger right)
    {
        var context = left.Workspace;
        var nextBuffer = new GpuUIntBigInteger(context, left.NumWords + right.NumWords);
        GpuUIntBigInteger? trimmed = null;
        try
        {
            Mul(left, right, nextBuffer);
            var actualWords = (GpuBigIntDivRem.GetActualBits(nextBuffer) + 31) / 32;
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

