namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Provides static mathematical and bitwise operations for <see cref="GpuBigInteger"/> on the GPU.
/// </summary>
public static class GpuBigIntMath
{
    private static void ThrowIfSizeMismatch(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        if (leftOperand.NumWords != rightOperand.NumWords || leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    private static void ThrowIfSizeMismatch(GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
    {
        if (leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must have the same size as input buffer. a: {leftOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    /// <summary>Computes arbitrary-precision addition (<c>leftOperand + rightOperand</c>) on the GPU.</summary>
    public static void Add(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.Add(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Adds a scalar 32-bit unsigned integer to <paramref name="leftOperand"/> on the GPU.</summary>
    public static void Add(GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        leftOperand.Context.Provider.Add(leftOperand.Context, leftOperand, rightOperand, resultBuffer);

    /// <summary>Computes arbitrary-precision subtraction (<c>leftOperand - rightOperand</c>) on the GPU.</summary>
    public static void Sub(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.Sub(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Subtracts a scalar 32-bit unsigned integer from <paramref name="leftOperand"/> on the GPU.</summary>
    public static void Sub(GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        leftOperand.Context.Provider.Sub(leftOperand.Context, leftOperand, rightOperand, resultBuffer);

    /// <summary>
    /// Computes full arbitrary-precision multiplication (<c>leftOperand * rightOperand</c>) on the GPU.
    /// </summary>
    /// <remarks>The destination buffer must have length equal to <c>leftOperand.NumWords + rightOperand.NumWords</c>.</remarks>
    public static void Mul(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        if (resultBuffer.NumWords != leftOperand.NumWords + rightOperand.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must be the sum of input buffer sizes for Mul. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
        leftOperand.Context.Provider.Mul(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Multiplies <paramref name="leftOperand"/> by a scalar 32-bit unsigned integer on the GPU.</summary>
    public static void Mul(GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        leftOperand.Context.Provider.Mul(leftOperand.Context, leftOperand, rightOperand, resultBuffer);

    /// <summary>Computes the Greatest Common Divisor (GCD) of two big integers on the GPU.</summary>
    public static void Gcd(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.Gcd(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Shifts the big integer left by the specified bit count on the GPU.</summary>
    public static void ShiftLeft(GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Context.Provider.ShiftLeft(leftOperand.Context, leftOperand, shiftBits, resultBuffer);
    }

    /// <summary>Shifts the big integer right by the specified bit count on the GPU.</summary>
    public static void ShiftRight(GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Context.Provider.ShiftRight(leftOperand.Context, leftOperand, shiftBits, resultBuffer);
    }

    /// <summary>Performs bitwise AND (<c>leftOperand &amp; rightOperand</c>) on GPU and stores result in destination buffer.</summary>
    public static void BitwiseAnd(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseAnd(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Performs bitwise OR on the GPU.</summary>
    public static void BitwiseOr(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseOr(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Performs bitwise XOR on the GPU.</summary>
    public static void BitwiseXor(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseXor(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Performs bitwise NOT on the GPU.</summary>
    public static void BitwiseNot(GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseNot(leftOperand.Context, leftOperand, resultBuffer);
    }

    /// <summary>Determines whether <paramref name="leftOperand"/> is strictly less than <paramref name="rightOperand"/> on the GPU.</summary>
    public static bool IsLessThan(GpuBigInteger leftOperand, GpuBigInteger rightOperand)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand);
        using var temp = new GpuBigInteger(leftOperand.Context, leftOperand.NumWords);
        Sub(leftOperand, rightOperand, temp);
        var topWord = temp.GetTopWords(1)[0];
        return (topWord & 0x80000000u) != 0;
    }

    /// <summary>
    /// Computes 10 raised to the specified exponent on the GPU using exponentiation by squaring.
    /// </summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="exponent">The exponent.</param>
    /// <returns>A new <see cref="GpuBigInteger"/> containing the result.</returns>
    public static GpuBigInteger Pow10(GpuContext context, int exponent)
    {
        const int MaxPow10Exponent = 50_000_000;
        if (exponent < 0 || exponent > MaxPow10Exponent)
        {
            throw new ArgumentOutOfRangeException(nameof(exponent), $"Exponent must be between 0 and {MaxPow10Exponent}.");
        }

        if (exponent <= 9)
        {
            return GpuBigInteger.FromBigInteger(context, BigInteger.Pow(10, exponent));
        }

        var resultGpu = GpuBigInteger.FromBigInteger(context, 1);
        GpuBigInteger? currentGpu = null;
        var success = false;
        try
        {
            currentGpu = GpuBigInteger.FromBigInteger(context, 10);
            var exp = exponent;

            while (exp > 0)
            {
                if ((exp & 1) == 1)
                {
                    var nextResult = MulAndTrim(resultGpu, currentGpu);
                    resultGpu.Dispose();
                    resultGpu = nextResult;
                }

                exp >>= 1;
                if (exp > 0)
                {
                    var nextCurrent = MulAndTrim(currentGpu, currentGpu);
                    currentGpu.Dispose();
                    currentGpu = nextCurrent;
                }
            }
            
            success = true;
            return resultGpu;
        }
        finally
        {
            currentGpu?.Dispose();
            if (!success)
            {
                resultGpu?.Dispose();
            }
        }
    }

    private static GpuBigInteger MulAndTrim(GpuBigInteger left, GpuBigInteger right)
    {
        var context = left.Context;
        var nextBuffer = new GpuBigInteger(context, left.NumWords + right.NumWords);
        GpuBigInteger? trimmed = null;
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
            return result;
        }
        finally
        {
            nextBuffer.Dispose();
            trimmed?.Dispose();
        }
    }
}

