using System;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

/// <summary>
/// A value type wrapper that builds a lazy expression tree of GPU signed BigInteger arithmetic.
/// Note that due to the lazy evaluation of absolute magnitudes, operations where the resulting
/// sign cannot be predicted (e.g., adding a positive and negative number) are not supported.
/// </summary>
public readonly struct GpuBigIntExpr
{
    /// <summary>
    /// Gets the underlying unsigned expression representing the absolute value.
    /// </summary>
    public GpuBigUIntExpr AbsoluteExpr { get; }

    /// <summary>
    /// Gets the predicted sign flag of the evaluated result.
    /// </summary>
    public bool IsNegative { get; }

    internal GpuBigIntExpr(GpuBigUIntExpr absoluteExpr, bool isNegative)
    {
        AbsoluteExpr = absoluteExpr;
        IsNegative = isNegative;
    }

    /// <summary>Implicitly wraps a <see cref="GpuBigInteger"/> as a signed expression leaf node.</summary>
    public static implicit operator GpuBigIntExpr(GpuBigInteger b)
        => new(b.Value, b.IsNegative);

    /// <summary>Constructs a lazy multiplication expression. Sign is determined by XOR.</summary>
    public static GpuBigIntExpr operator *(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(left.AbsoluteExpr * right.AbsoluteExpr, left.IsNegative ^ right.IsNegative);

    /// <summary>Constructs a lazy division expression. Sign is determined by XOR.</summary>
    public static GpuBigIntExpr operator /(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(left.AbsoluteExpr / right.AbsoluteExpr, left.IsNegative ^ right.IsNegative);

    /// <summary>Constructs a lazy addition expression for operands with the same sign.</summary>
    public static GpuBigIntExpr operator +(GpuBigIntExpr left, GpuBigIntExpr right)
    {
        if (left.IsNegative == right.IsNegative)
        {
            return new GpuBigIntExpr(left.AbsoluteExpr + right.AbsoluteExpr, left.IsNegative);
        }
        
        throw new NotSupportedException("Signed addition with different signs cannot be represented in a lazy AST because the resulting sign depends on the magnitudes, which are not yet evaluated.");
    }

    /// <summary>Constructs a lazy subtraction expression for operands with different signs.</summary>
    public static GpuBigIntExpr operator -(GpuBigIntExpr left, GpuBigIntExpr right)
    {
        if (left.IsNegative != right.IsNegative)
        {
            return new GpuBigIntExpr(left.AbsoluteExpr + right.AbsoluteExpr, left.IsNegative);
        }

        throw new NotSupportedException("Signed subtraction with the same signs cannot be represented in a lazy AST because the resulting sign depends on the magnitudes, which are not yet evaluated.");
    }

    /// <summary>Constructs a lazy negation expression.</summary>
    public static GpuBigIntExpr operator -(GpuBigIntExpr left)
        => new(left.AbsoluteExpr, !left.IsNegative);

    /// <summary>Constructs a lazy bitwise NOT expression.</summary>
    public static GpuBigIntExpr operator ~(GpuBigIntExpr left)
        => new(~left.AbsoluteExpr, left.IsNegative);
}
