using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;
using System;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// A lightweight value type wrapping a <see cref="GpuUIntBigInteger"/> with a sign bit,
/// used for tracking alternating series or arbitrary signed integers on the GPU.
/// </summary>
public readonly struct GpuBigInteger : IDisposable, IEquatable<GpuBigInteger>, IComparable<GpuBigInteger>
{
    public GpuBigInteger([Kassyi.GSharp.Core.TakesOwnership] GpuUIntBigInteger value, bool isNegative)
    {
        Value = value;
        IsNegative = isNegative;
    }

    /// <summary>
    /// Gets the underlying absolute value buffer on the GPU.
    /// </summary>
    public GpuUIntBigInteger Value { get; }

    /// <summary>
    /// Gets a value indicating whether the integer is strictly negative.
    /// </summary>
    public bool IsNegative { get; }

    /// <summary>
    /// Disposes the underlying GPU buffer.
    /// </summary>
    public void Dispose() => Value.Dispose();

    public int CompareTo(GpuBigInteger other)
    {
        bool thisZero = Value.IsZero();
        bool otherZero = other.Value.IsZero();

        int absCmp = Value.CompareTo(other.Value);

        int sameSignCmp = IsNegative ? -absCmp : absCmp;
        int diffSignCmp = IsNegative ? -1 : 1;
        int normalCmp = (IsNegative != other.IsNegative) ? diffSignCmp : sameSignCmp;

        int thisZeroCmp = other.IsNegative ? 1 : -1;
        int otherZeroCmp = IsNegative ? -1 : 1;

        int zeroCmp = thisZero ? thisZeroCmp : (otherZero ? otherZeroCmp : normalCmp);

        return (thisZero && otherZero) ? 0 : zeroCmp;
    }

    public bool Equals(GpuBigInteger other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is GpuBigInteger other && Equals(other);

    public override int GetHashCode() => Value.IsZero() ? 0 : HashCode.Combine(Value.GetHashCode(), IsNegative);

    public static bool operator ==(GpuBigInteger left, GpuBigInteger right) => left.Equals(right);
    public static bool operator !=(GpuBigInteger left, GpuBigInteger right) => !left.Equals(right);
    public static bool operator <(GpuBigInteger left, GpuBigInteger right) => left.CompareTo(right) < 0;
    public static bool operator >(GpuBigInteger left, GpuBigInteger right) => left.CompareTo(right) > 0;
    public static bool operator <=(GpuBigInteger left, GpuBigInteger right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuBigInteger left, GpuBigInteger right) => left.CompareTo(right) >= 0;

    public static GpuBigIntExpr operator +(GpuBigInteger left, GpuBigInteger right) =>
        left + (GpuBigIntExpr)right;

    public static GpuBigIntExpr operator -(GpuBigInteger left, GpuBigInteger right) =>
        left - (GpuBigIntExpr)right;

    public static GpuBigIntExpr operator *(GpuBigInteger left, GpuBigInteger right) =>
        left * (GpuBigIntExpr)right;

    public static GpuBigIntExpr operator /(GpuBigInteger left, GpuBigInteger right) =>
        left / (GpuBigIntExpr)right;
}