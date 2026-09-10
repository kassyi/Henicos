using System;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// A lightweight value type wrapping a <see cref="GpuUIntBigInteger"/> with a sign bit,
/// used for tracking alternating series or arbitrary signed integers on the GPU.
/// </summary>
public readonly struct GpuBigInteger(GpuUIntBigInteger value, bool isNegative)
    : IDisposable, IEquatable<GpuBigInteger>, IComparable<GpuBigInteger>
{
    /// <summary>
    /// Gets the underlying absolute value buffer on the GPU.
    /// </summary>
    public GpuUIntBigInteger Value { get; } = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>
    /// Gets a value indicating whether the integer is strictly negative.
    /// </summary>
    public bool IsNegative { get; } = isNegative;

    /// <summary>
    /// Disposes the underlying GPU buffer.
    /// </summary>
    public void Dispose()
    {
        Value.Dispose();
    }

    public int CompareTo(GpuBigInteger other)
    {
        var thisZero = Value.IsZero();
        var otherZero = other.Value.IsZero();

        if (thisZero && otherZero)
        {
            return 0;
        }

        if (thisZero)
        {
            return other.IsNegative ? 1 : -1;
        }

        if (otherZero)
        {
            return IsNegative ? -1 : 1;
        }

        if (IsNegative != other.IsNegative)
        {
            return IsNegative ? -1 : 1;
        }

        var absCmp = Value.CompareTo(other.Value);
        return IsNegative ? -absCmp : absCmp;
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
