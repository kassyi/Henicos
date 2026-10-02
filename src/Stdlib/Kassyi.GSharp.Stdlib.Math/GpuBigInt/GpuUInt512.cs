namespace Kassyi.GSharp.Stdlib.Math.GpuBigInt;

/// <summary>
/// Represents a 512-bit unsigned integer stored on the GPU.
/// </summary>
[CollectionBuilder(typeof(GpuUInt512), nameof(Create))]
public struct GpuUInt512(GpuUIntBigInteger<LimbBuffer16> value) : IEquatable<GpuUInt512>, IComparable<GpuUInt512>
{
    public GpuUIntBigInteger<LimbBuffer16> Value = value;

    public GpuUInt512(uint value) : this(GpuUIntBuilder.Create<LimbBuffer16>([value])) { }

    public static implicit operator GpuUIntBigInteger<LimbBuffer16>(GpuUInt512 val) => val.Value;
    public static implicit operator GpuUInt512(GpuUIntBigInteger<LimbBuffer16> val) => new(val);
    public static implicit operator GpuUInt512(uint val) => new(val);

    public DummyUIntEnumerator GetEnumerator() => default;
    public static GpuUInt512 Create(ReadOnlySpan<uint> elements) => new(GpuUIntBuilder.Create<LimbBuffer16>(elements));

    public readonly bool Equals(GpuUInt512 other) => Value.Equals(other.Value);
    public readonly int CompareTo(GpuUInt512 other) => Value.CompareTo(other.Value);
    public readonly override bool Equals(object? obj) => obj is GpuUInt512 other && Equals(other);
    public readonly override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(GpuUInt512 left, GpuUInt512 right) => left.Equals(right);
    public static bool operator !=(GpuUInt512 left, GpuUInt512 right) => !left.Equals(right);
    public static bool operator <(GpuUInt512 left, GpuUInt512 right) => left.CompareTo(right) < 0;
    public static bool operator >(GpuUInt512 left, GpuUInt512 right) => left.CompareTo(right) > 0;
    public static bool operator <=(GpuUInt512 left, GpuUInt512 right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuUInt512 left, GpuUInt512 right) => left.CompareTo(right) >= 0;
}
