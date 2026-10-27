
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A lightweight value type wrapping an arbitrary-precision <see cref="GpuBigUInt"/> with a sign bit,
/// used for signed arbitrary-precision arithmetic and series summation on NVIDIA GPUs.
/// </summary>
/// <param name="value">The underlying <see cref="GpuBigUInt"/> representing the absolute magnitude. Takes ownership of the buffer.</param>
/// <param name="isNegative"><see langword="true"/> if the integer is negative; otherwise, <see langword="false"/>.</param>
/// <remarks>
/// <para><b>Performance:</b> Wraps a device memory handle with zero additional heap allocation on the host.</para>
/// <para><b>Memory Management:</b> Implements <see cref="IDisposable"/> to release the underlying GPU device memory allocated for <see cref="Value"/>.</para>
/// <para><b>Thread Safety:</b> Value-type semantics; instance members are not thread-safe for concurrent mutating operations on the underlying GPU memory.</para>
/// <para><b>Equality:</b> <see cref="Equals(GpuBigInt)"/> and <see cref="GetHashCode"/> tell whether two instances have the
/// same magnitude buffer and sign and never read device memory; <c>==</c>, <c>!=</c> and
/// <see cref="CompareTo(GpuBigInt)"/> compare values on the GPU (<c>+0</c> equals <c>-0</c>). The two differ on purpose,
/// as <c>==</c> and <see cref="double.Equals(double)"/> differ for <see cref="double.NaN"/>.</para>
/// </remarks>
public readonly struct GpuBigInt([TakesOwnership] GpuBigUInt value, bool isNegative)
    : IGpuBigNumber<GpuBigInt>
{
    /// <summary>
    /// Gets the underlying absolute value buffer stored in GPU device memory.
    /// </summary>
    public GpuBigUInt Value { get; } = value;

    /// <summary>
    /// Gets a value indicating whether the signed integer is strictly negative.
    /// </summary>
    public bool IsNegative { get; } = isNegative;

    /// <inheritdoc/>
    public bool CanBeNegative => true;

    /// <inheritdoc/>
    public GpuWorkspace Workspace => Value.Workspace;

    /// <inheritdoc/>
    public GpuBigUInt Magnitude => Value;

    /// <inheritdoc/>
    public int Exponent => 0;

    /// <inheritdoc/>
    public long EffectiveScale => Value.GetBitLength();

    /// <inheritdoc/>
    public bool IsZero() => Value.IsZero();

    /// <inheritdoc/>
    public int GetBitLength() => Value.GetBitLength();

    /// <inheritdoc/>
    public static GpuBigInt Create([TakesOwnership] GpuBigUInt magnitude, int exponent, bool isNegative)
    {
        if (exponent != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exponent), "Integer exponent must be zero.");
        }

        return new GpuBigInt(magnitude, isNegative && !magnitude.IsZero());
    }

    /// <summary>
    /// Releases the GPU global device memory resources allocated for the underlying <see cref="Value"/>.
    /// </summary>
    public void Dispose() => Value.Dispose();

    /// <summary>
    /// Compares this instance to another <see cref="GpuBigInt"/> and returns an integer that indicates whether the value of this instance is less than, equal to, or greater than the value of the specified instance.
    /// </summary>
    /// <param name="other">A <see cref="GpuBigInt"/> to compare with this instance.</param>
    /// <returns>
    /// A signed integer that indicates the relative values of this instance and <paramref name="other"/>:
    /// Less than zero if this instance is less than <paramref name="other"/>,
    /// Zero if this instance equals <paramref name="other"/>,
    /// Greater than zero if this instance is greater than <paramref name="other"/>.
    /// </returns>
    public int CompareTo(GpuBigInt other) => GpuBigNumberAlgorithms.Compare(this, other);

    /// <summary>
    /// Indicates whether the current instance has the same magnitude buffer and the same sign as another
    /// <see cref="GpuBigInt"/>.
    /// </summary>
    /// <param name="other">A <see cref="GpuBigInt"/> to compare with this instance.</param>
    /// <returns>
    /// <see langword="true"/> if <see cref="Value"/> refers to the same buffer (<see cref="GpuBigUInt.Equals(GpuBigUInt)"/>)
    /// and <see cref="IsNegative"/> is the same; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Unlike the value-equal float types, <see cref="Equals(GpuBigInt)"/> and <see cref="GetHashCode"/> compare identity:
    /// two separate buffers holding the same value (including <c>+0</c> and <c>-0</c>) are not equal. Use <c>==</c> or
    /// <see cref="CompareTo(GpuBigInt)"/> to compare values on the GPU. Neither member reads device memory, so both also
    /// work on a disposed value.
    /// </remarks>
    public bool Equals(GpuBigInt other) => Value.Equals(other.Value) && IsNegative == other.IsNegative;

    /// <summary>
    /// Determines whether the specified object is equal to the current instance.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="GpuBigInt"/> and equals the current instance; otherwise, <see langword="false"/>.</returns>
    public override bool Equals(object? obj) => obj is GpuBigInt other && Equals(other);

    /// <summary>
    /// Returns the hash code of the buffer identity and the sign, consistent with <see cref="Equals(GpuBigInt)"/>.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Value.GetHashCode(), IsNegative);

    /// <summary>
    /// Determines whether two specified <see cref="GpuBigInt"/> instances have the same value on the GPU.
    /// </summary>
    /// <param name="left">The first <see cref="GpuBigInt"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuBigInt"/> to compare.</param>
    /// <returns><see langword="true"/> if the values are equal (<c>+0</c> equals <c>-0</c>); otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(GpuBigInt left, GpuBigInt right) => left.CompareTo(right) == 0;

    /// <summary>
    /// Determines whether two specified <see cref="GpuBigInt"/> instances have different values on the GPU.
    /// </summary>
    /// <param name="left">The first <see cref="GpuBigInt"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuBigInt"/> to compare.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(GpuBigInt left, GpuBigInt right) => left.CompareTo(right) != 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigInt"/> is less than another specified <see cref="GpuBigInt"/>.
    /// </summary>
    /// <param name="left">The first <see cref="GpuBigInt"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuBigInt"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <(GpuBigInt left, GpuBigInt right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigInt"/> is greater than another specified <see cref="GpuBigInt"/>.
    /// </summary>
    /// <param name="left">The first <see cref="GpuBigInt"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuBigInt"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >(GpuBigInt left, GpuBigInt right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigInt"/> is less than or equal to another specified <see cref="GpuBigInt"/>.
    /// </summary>
    /// <param name="left">The first <see cref="GpuBigInt"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuBigInt"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <=(GpuBigInt left, GpuBigInt right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigInt"/> is greater than or equal to another specified <see cref="GpuBigInt"/>.
    /// </summary>
    /// <param name="left">The first <see cref="GpuBigInt"/> to compare.</param>
    /// <param name="right">The second <see cref="GpuBigInt"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >=(GpuBigInt left, GpuBigInt right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Creates a deferred expression representing the addition of two signed big integers.
    /// </summary>
    /// <param name="left">The left signed operand.</param>
    /// <param name="right">The right signed operand.</param>
    /// <returns>A <see cref="GpuBigIntExpr"/> AST node capturing the addition.</returns>
    public static GpuBigIntExpr operator +(GpuBigInt left, GpuBigInt right) =>
        left + (GpuBigIntExpr)right;

    /// <summary>
    /// Creates a deferred expression representing the subtraction of two signed big integers.
    /// </summary>
    /// <param name="left">The left signed operand (minuend).</param>
    /// <param name="right">The right signed operand (subtrahend).</param>
    /// <returns>A <see cref="GpuBigIntExpr"/> AST node capturing the subtraction.</returns>
    public static GpuBigIntExpr operator -(GpuBigInt left, GpuBigInt right) =>
        left - (GpuBigIntExpr)right;

    /// <summary>
    /// Creates a deferred expression representing the multiplication of two signed big integers.
    /// </summary>
    /// <param name="left">The left signed operand.</param>
    /// <param name="right">The right signed operand.</param>
    /// <returns>A <see cref="GpuBigIntExpr"/> AST node capturing the multiplication.</returns>
    public static GpuBigIntExpr operator *(GpuBigInt left, GpuBigInt right) =>
        left * (GpuBigIntExpr)right;

    /// <summary>
    /// Creates a deferred expression representing the division of two signed big integers.
    /// </summary>
    /// <param name="left">The left signed operand (dividend).</param>
    /// <param name="right">The right signed operand (divisor).</param>
    /// <returns>A <see cref="GpuBigIntExpr"/> AST node capturing the division.</returns>
    public static GpuBigIntExpr operator /(GpuBigInt left, GpuBigInt right) =>
        left / (GpuBigIntExpr)right;

    /// <summary>
    /// Creates a deferred expression representing the remainder (modulo) of two signed big integers.
    /// </summary>
    /// <param name="left">The left signed operand (dividend).</param>
    /// <param name="right">The right signed operand (divisor).</param>
    /// <returns>A <see cref="GpuBigIntExpr"/> AST node capturing the modulo operation.</returns>
    public static GpuBigIntExpr operator %(GpuBigInt left, GpuBigInt right) =>
        left % (GpuBigIntExpr)right;

    /// <inheritdoc/>
    static GpuBigInt IGpuBigNumber<GpuBigInt>.operator +(GpuBigInt left, GpuBigInt right) =>
        (left + right).Evaluate(left.Workspace);

    /// <inheritdoc/>
    static GpuBigInt IGpuBigNumber<GpuBigInt>.operator -(GpuBigInt left, GpuBigInt right) =>
        (left - right).Evaluate(left.Workspace);

    /// <inheritdoc/>
    static GpuBigInt IGpuBigNumber<GpuBigInt>.operator *(GpuBigInt left, GpuBigInt right) =>
        (left * right).Evaluate(left.Workspace);

    /// <inheritdoc/>
    static GpuBigInt IGpuBigNumber<GpuBigInt>.operator /(GpuBigInt left, GpuBigInt right) =>
        (left / right).Evaluate(left.Workspace);
}
