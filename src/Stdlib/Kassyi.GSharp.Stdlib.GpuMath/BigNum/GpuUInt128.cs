namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents a fixed-width 128-bit unsigned integer stored in value-type memory on the GPU.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Zero-allocation value type backed by a 4-word (128-bit) unmanaged limb buffer.</para>
/// <para><b>Execution Target:</b> Safe for execution in both host and GPU device kernels.</para>
/// <para><b>Thread Safety:</b> Immutable-style value semantics; concurrent reads are safe.</para>
/// </remarks>
[CollectionBuilder(typeof(GpuUInt128), nameof(Create))]
public struct GpuUInt128(GpuBigUInt<LimbBuffer4> value) : IEquatable<GpuUInt128>, IComparable<GpuUInt128>
{
    /// <summary>
    /// The underlying 128-bit fixed-size unsigned big integer storage.
    /// </summary>
    public GpuBigUInt<LimbBuffer4> Value = value;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuUInt128"/> struct initialized with a 32-bit unsigned integer value.
    /// </summary>
    /// <param name="value">The 32-bit unsigned integer value.</param>
    public GpuUInt128(uint value) : this(GpuBigUIntBuilder.Create<LimbBuffer4>([value])) { }

    /// <summary>
    /// Implicitly converts a <see cref="GpuUInt128"/> instance to its underlying <see cref="GpuBigUInt{LimbBuffer4}"/> representation.
    /// </summary>
    /// <param name="val">The <see cref="GpuUInt128"/> instance to convert.</param>
    /// <returns>The underlying <see cref="GpuBigUInt{LimbBuffer4}"/> value.</returns>
    public static implicit operator GpuBigUInt<LimbBuffer4>(GpuUInt128 val) => val.Value;

    /// <summary>
    /// Implicitly converts a <see cref="GpuBigUInt{LimbBuffer4}"/> value to a <see cref="GpuUInt128"/> instance.
    /// </summary>
    /// <param name="val">The <see cref="GpuBigUInt{LimbBuffer4}"/> value to wrap.</param>
    /// <returns>A new <see cref="GpuUInt128"/> wrapping the specified limbs.</returns>
    public static implicit operator GpuUInt128(GpuBigUInt<LimbBuffer4> val) => new(val);

    /// <summary>
    /// Implicitly converts a 32-bit unsigned integer to a <see cref="GpuUInt128"/> instance.
    /// </summary>
    /// <param name="val">The 32-bit unsigned integer.</param>
    /// <returns>A new <see cref="GpuUInt128"/> initialized with the specified value.</returns>
    public static implicit operator GpuUInt128(uint val) => new(val);

    /// <summary>
    /// Returns a dummy enumerator to enable C# collection expression support.
    /// </summary>
    /// <returns>A default instance of <see cref="DummyUIntEnumerator"/>.</returns>
    public DummyUIntEnumerator GetEnumerator() => default;

    /// <summary>
    /// Creates a new <see cref="GpuUInt128"/> instance from the specified sequence of 32-bit limb words.
    /// </summary>
    /// <param name="elements">A read-only span containing the 32-bit unsigned limbs in little-endian order.</param>
    /// <returns>A new <see cref="GpuUInt128"/> instance containing the specified elements.</returns>
    public static GpuUInt128 Create(ReadOnlySpan<uint> elements) => new(GpuBigUIntBuilder.Create<LimbBuffer4>(elements));

    /// <summary>
    /// Indicates whether the current instance is equal to another <see cref="GpuUInt128"/> instance.
    /// </summary>
    /// <param name="other">A <see cref="GpuUInt128"/> to compare with this instance.</param>
    /// <returns><see langword="true"/> if the current instance is equal to the <paramref name="other"/> parameter; otherwise, <see langword="false"/>.</returns>
    public readonly bool Equals(GpuUInt128 other) => Value.Equals(other.Value);

    /// <summary>
    /// Compares this instance to another <see cref="GpuUInt128"/> and returns an integer that indicates whether the value of this instance is less than, equal to, or greater than the value of the specified instance.
    /// </summary>
    /// <param name="other">A <see cref="GpuUInt128"/> to compare.</param>
    /// <returns>
    /// A signed integer that indicates the relative values of this instance and <paramref name="other"/>:
    /// Less than zero if this instance is less than <paramref name="other"/>,
    /// Zero if this instance equals <paramref name="other"/>,
    /// Greater than zero if this instance is greater than <paramref name="other"/>.
    /// </returns>
    public readonly int CompareTo(GpuUInt128 other) => Value.CompareTo(other.Value);

    /// <summary>
    /// Determines whether the specified object is equal to the current instance.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="GpuUInt128"/> and equals the current instance; otherwise, <see langword="false"/>.</returns>
    public readonly override bool Equals(object? obj) => obj is GpuUInt128 other && Equals(other);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public readonly override int GetHashCode() => Value.GetHashCode();

    /// <summary>
    /// Determines whether two specified <see cref="GpuUInt128"/> instances are equal.
    /// </summary>
    /// <param name="left">The first <see cref="GpuUInt128"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuUInt128"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> equals <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(GpuUInt128 left, GpuUInt128 right) => left.Equals(right);

    /// <summary>
    /// Determines whether two specified <see cref="GpuUInt128"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first <see cref="GpuUInt128"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuUInt128"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not equal <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(GpuUInt128 left, GpuUInt128 right) => !left.Equals(right);

    /// <summary>
    /// Determines whether one specified <see cref="GpuUInt128"/> is less than another specified <see cref="GpuUInt128"/>.
    /// </summary>
    /// <param name="left">The first <see cref="GpuUInt128"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuUInt128"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <(GpuUInt128 left, GpuUInt128 right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuUInt128"/> is greater than another specified <see cref="GpuUInt128"/>.
    /// </summary>
    /// <param name="left">The first <see cref="GpuUInt128"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuUInt128"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >(GpuUInt128 left, GpuUInt128 right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuUInt128"/> is less than or equal to another specified <see cref="GpuUInt128"/>.
    /// </summary>
    /// <param name="left">The first <see cref="GpuUInt128"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuUInt128"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <=(GpuUInt128 left, GpuUInt128 right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuUInt128"/> is greater than or equal to another specified <see cref="GpuUInt128"/>.
    /// </summary>
    /// <param name="left">The first <see cref="GpuUInt128"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuUInt128"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >=(GpuUInt128 left, GpuUInt128 right) => left.CompareTo(right) >= 0;
}
