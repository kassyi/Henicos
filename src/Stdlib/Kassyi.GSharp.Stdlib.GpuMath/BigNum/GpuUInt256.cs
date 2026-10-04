namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents a 256-bit unsigned integer stored on the GPU.
/// </summary>
[CollectionBuilder(typeof(GpuUInt256), nameof(Create))]
public struct GpuUInt256(GpuBigUInt<LimbBuffer8> value) : IEquatable<GpuUInt256>, IComparable<GpuUInt256>
{
    public GpuBigUInt<LimbBuffer8> Value = value;

    public GpuUInt256(uint value) : this(GpuBigUIntBuilder.Create<LimbBuffer8>([value])) { }

    public static implicit operator GpuBigUInt<LimbBuffer8>(GpuUInt256 val) => val.Value;
    public static implicit operator GpuUInt256(GpuBigUInt<LimbBuffer8> val) => new(val);
    public static implicit operator GpuUInt256(uint val) => new(val);

    public DummyUIntEnumerator GetEnumerator() => default;
    public static GpuUInt256 Create(ReadOnlySpan<uint> elements) => new(GpuBigUIntBuilder.Create<LimbBuffer8>(elements));

    public readonly bool Equals(GpuUInt256 other) => Value.Equals(other.Value);
    public readonly int CompareTo(GpuUInt256 other) => Value.CompareTo(other.Value);
    public readonly override bool Equals(object? obj) => obj is GpuUInt256 other && Equals(other);
    public readonly override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(GpuUInt256 left, GpuUInt256 right) => left.Equals(right);
    public static bool operator !=(GpuUInt256 left, GpuUInt256 right) => !left.Equals(right);
    public static bool operator <(GpuUInt256 left, GpuUInt256 right) => left.CompareTo(right) < 0;
    public static bool operator >(GpuUInt256 left, GpuUInt256 right) => left.CompareTo(right) > 0;
    public static bool operator <=(GpuUInt256 left, GpuUInt256 right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuUInt256 left, GpuUInt256 right) => left.CompareTo(right) >= 0;
}
