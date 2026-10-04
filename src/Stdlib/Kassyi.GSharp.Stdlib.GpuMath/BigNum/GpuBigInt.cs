using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A lightweight value type wrapping a <see cref="GpuBigUInt"/> with a sign bit,
/// used for tracking alternating series or arbitrary signed integers on the GPU.
/// </summary>
public readonly struct GpuBigInt([TakesOwnership] GpuBigUInt value, bool isNegative)
    : IEquatable<GpuBigInt>, IComparable<GpuBigInt>, IDisposable
{
    /// <summary>
    /// Gets the underlying absolute value buffer on the GPU.
    /// </summary>
    public GpuBigUInt Value { get; } = value;

    /// <summary>
    /// Gets a value indicating whether the integer is strictly negative.
    /// </summary>
    public bool IsNegative { get; } = isNegative;

    /// <summary>
    /// Disposes the underlying GPU memory.
    /// </summary>
    public void Dispose() => Value.Dispose();

    public int CompareTo(GpuBigInt other)
    {
        var thisZero = Value.IsZero();
        var otherZero = other.Value.IsZero();

        var absCmp = Value.CompareTo(other.Value);

        var sameSignCmp = IsNegative ? -absCmp : absCmp;
        var diffSignCmp = IsNegative ? -1 : 1;
        var normalCmp = (IsNegative != other.IsNegative) ? diffSignCmp : sameSignCmp;

        var thisZeroCmp = other.IsNegative ? 1 : -1;
        var otherZeroCmp = IsNegative ? -1 : 1;

        var zeroCmp = thisZero ? thisZeroCmp : (otherZero ? otherZeroCmp : normalCmp);

        return (thisZero && otherZero) ? 0 : zeroCmp;
    }

    public bool Equals(GpuBigInt other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is GpuBigInt other && Equals(other);

    public override int GetHashCode() => Value.IsZero() ? 0 : HashCode.Combine(Value.GetHashCode(), IsNegative);

    public static bool operator ==(GpuBigInt left, GpuBigInt right) => left.Equals(right);
    public static bool operator !=(GpuBigInt left, GpuBigInt right) => !left.Equals(right);
    public static bool operator <(GpuBigInt left, GpuBigInt right) => left.CompareTo(right) < 0;
    public static bool operator >(GpuBigInt left, GpuBigInt right) => left.CompareTo(right) > 0;
    public static bool operator <=(GpuBigInt left, GpuBigInt right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuBigInt left, GpuBigInt right) => left.CompareTo(right) >= 0;

    public static GpuBigIntExpr operator +(GpuBigInt left, GpuBigInt right) =>
        left + (GpuBigIntExpr)right;

    public static GpuBigIntExpr operator -(GpuBigInt left, GpuBigInt right) =>
        left - (GpuBigIntExpr)right;

    public static GpuBigIntExpr operator *(GpuBigInt left, GpuBigInt right) =>
        left * (GpuBigIntExpr)right;

    public static GpuBigIntExpr operator /(GpuBigInt left, GpuBigInt right) =>
        left / (GpuBigIntExpr)right;
}
