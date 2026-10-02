namespace Kassyi.GSharp.Stdlib.Math.GpuBigInt;

/// <summary>
/// Represents a 128-bit unsigned integer stored on the GPU.
/// </summary>
[CollectionBuilder(typeof(GpuUInt128), nameof(Create))]
public struct GpuUInt128(GpuUIntBigInteger<LimbBuffer4> value) : IEquatable<GpuUInt128>, IComparable<GpuUInt128>
{
    public GpuUIntBigInteger<LimbBuffer4> Value = value;

    public GpuUInt128(uint value) : this(GpuUIntBuilder.Create<LimbBuffer4>([value])) { }

    public static implicit operator GpuUIntBigInteger<LimbBuffer4>(GpuUInt128 val) => val.Value;
    public static implicit operator GpuUInt128(GpuUIntBigInteger<LimbBuffer4> val) => new(val);
    public static implicit operator GpuUInt128(uint val) => new(val);

    public DummyUIntEnumerator GetEnumerator() => default;
    public static GpuUInt128 Create(ReadOnlySpan<uint> elements) => new(GpuUIntBuilder.Create<LimbBuffer4>(elements));

    public readonly bool Equals(GpuUInt128 other) => Value.Equals(other.Value);
    public readonly int CompareTo(GpuUInt128 other) => Value.CompareTo(other.Value);
    public readonly override bool Equals(object? obj) => obj is GpuUInt128 other && Equals(other);
    public readonly override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(GpuUInt128 left, GpuUInt128 right) => left.Equals(right);
    public static bool operator !=(GpuUInt128 left, GpuUInt128 right) => !left.Equals(right);
    public static bool operator <(GpuUInt128 left, GpuUInt128 right) => left.CompareTo(right) < 0;
    public static bool operator >(GpuUInt128 left, GpuUInt128 right) => left.CompareTo(right) > 0;
    public static bool operator <=(GpuUInt128 left, GpuUInt128 right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuUInt128 left, GpuUInt128 right) => left.CompareTo(right) >= 0;
}
