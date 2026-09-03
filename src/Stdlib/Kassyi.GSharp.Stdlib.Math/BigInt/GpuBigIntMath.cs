using System;

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
}

