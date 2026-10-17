
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents an arbitrary-precision signed floating-point number on the GPU using an unsigned floating-point value and a sign flag.
/// </summary>
/// <remarks>
/// <para><b>Representation:</b> Represented as $(-1)^{\text{IsNegative}} \times \text{UFloat.Significand} \times 2^{\text{UFloat.Exponent}}$.</para>
/// <para><b>Performance:</b> Backed by device memory allocations in <see cref="GpuBigUInt"/> significand with zero heap overhead on the host.</para>
/// <para><b>Thread Safety:</b> Value type semantics; distinct instances are safe for concurrent read operations.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
[ExcludeFromCodeCoverage]
public readonly struct GpuBigFloat : IEquatable<GpuBigFloat>, IComparable<GpuBigFloat>, IDisposable
{
    /// <summary>
    /// Gets the underlying unsigned floating-point magnitude payload.
    /// </summary>
    public GpuBigUFloat UFloat
    {
        get;
    }

    /// <summary>
    /// Gets a value indicating whether the floating-point number is negative.
    /// </summary>
    public bool IsNegative
    {
        get;
    }

    /// <summary>
    /// Releases the GPU global device memory resources allocated for the underlying significand.
    /// </summary>
    public void Dispose() => UFloat.Dispose();

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigFloat"/> struct from an unsigned float magnitude and a sign flag.
    /// </summary>
    /// <param name="uFloat">The unsigned float component. Takes ownership of the underlying buffer.</param>
    /// <param name="isNegative"><see langword="true"/> if the value is negative; otherwise, <see langword="false"/>.</param>
    public GpuBigFloat([TakesOwnership] GpuBigUFloat uFloat, bool isNegative)
    {
        UFloat = uFloat;
        IsNegative = isNegative;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigFloat"/> struct from significand, binary exponent, and sign flag.
    /// </summary>
    /// <param name="significand">The significand payload. Takes ownership of the buffer.</param>
    /// <param name="exponent">The binary base-2 exponent.</param>
    /// <param name="isNegative"><see langword="true"/> if the value is negative; otherwise, <see langword="false"/>.</param>
    public GpuBigFloat([TakesOwnership] GpuBigUInt significand, int exponent, bool isNegative)
        : this(new GpuBigUFloat(significand, exponent), isNegative)
    {
    }

    /// <summary>
    /// Creates a deferred expression representing the addition of two floating-point values.
    /// </summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <returns>A binary expression node representing the addition.</returns>
    public static BinaryExpr<GpuBigFloat, AddOp> operator +(GpuBigFloat left, GpuBigFloat right) => new(left, right);

    /// <summary>
    /// Creates a deferred expression representing the subtraction of two floating-point values.
    /// </summary>
    /// <param name="left">The minuend operand.</param>
    /// <param name="right">The subtrahend operand.</param>
    /// <returns>A binary expression node representing the subtraction.</returns>
    public static BinaryExpr<GpuBigFloat, SubOp> operator -(GpuBigFloat left, GpuBigFloat right) => new(left, right);

    /// <summary>
    /// Creates a deferred expression representing the multiplication of two floating-point values.
    /// </summary>
    /// <param name="left">The multiplicand operand.</param>
    /// <param name="right">The multiplier operand.</param>
    /// <returns>A binary expression node representing the multiplication.</returns>
    public static BinaryExpr<GpuBigFloat, MulOp> operator *(GpuBigFloat left, GpuBigFloat right) => new(left, right);

    /// <summary>
    /// Creates a deferred expression representing the division of two floating-point values.
    /// </summary>
    /// <param name="left">The dividend operand.</param>
    /// <param name="right">The divisor operand.</param>
    /// <returns>A binary expression node representing the division.</returns>
    public static BinaryExpr<GpuBigFloat, DivOp> operator /(GpuBigFloat left, GpuBigFloat right) => new(left, right);

    /// <summary>
    /// Aligns the radix points (exponents) of this instance and another floating-point number, extracting IEEE 754 rounding bits.
    /// </summary>
    /// <param name="other">The other floating-point instance to align with.</param>
    /// <returns>A tuple containing the aligned values and IEEE 754 guard, round, and sticky bits.</returns>
    public (GpuBigFloat AlignedThis, GpuBigFloat AlignedOther, bool Guard, bool Round, bool StickyThis, bool StickyOther
        ) AlignRadixPoint(GpuBigFloat other) => throw new NotImplementedException();

    /// <summary>
    /// Normalizes the floating-point value such that the most significant bit of the significand is aligned.
    /// </summary>
    /// <param name="leadingZeros">The number of leading zero bits in the significand.</param>
    /// <param name="carryOut"><see langword="true"/> if an arithmetic carry out occurred during addition; otherwise, <see langword="false"/>.</param>
    /// <returns>The normalized <see cref="GpuBigFloat"/> instance.</returns>
    public GpuBigFloat Normalize(int leadingZeros = 0, bool carryOut = false) =>
        new(UFloat.Normalize(leadingZeros, carryOut), IsNegative);

    /// <summary>
    /// Divides the floating-point number by a 32-bit unsigned integer scalar.
    /// </summary>
    /// <param name="divisor">The 32-bit unsigned integer divisor.</param>
    /// <returns>A tuple containing the quotient <see cref="GpuBigFloat"/> and the 32-bit integer remainder.</returns>
    public (GpuBigFloat Quotient, uint Remainder) DivideByUInt32(uint divisor)
    {
        var (uQuotient, remainder) = UFloat.DivideByUInt32(divisor);
        return (new GpuBigFloat(uQuotient, IsNegative), remainder);
    }

    /// <summary>
    /// Rounds the floating-point value to the target significand precision using the specified rounding mode and discarded bit flags.
    /// </summary>
    /// <param name="mode">The IEEE 754 rounding mode to apply.</param>
    /// <param name="guardBit">The first discarded bit beyond the target precision.</param>
    /// <param name="roundBit">The second discarded bit beyond the target precision.</param>
    /// <param name="stickyBit"><see langword="true"/> if any remaining discarded bits beyond <paramref name="roundBit"/> were non-zero.</param>
    /// <returns>The rounded <see cref="GpuBigFloat"/> instance.</returns>
    public GpuBigFloat Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit) =>
        throw new NotImplementedException();

    /// <summary>
    /// Adds another <see cref="GpuBigFloat"/> to this instance.
    /// </summary>
    /// <param name="other">The value to add.</param>
    /// <returns>The sum of this instance and <paramref name="other"/>.</returns>
    public GpuBigFloat Add(GpuBigFloat other) => this + other;

    /// <summary>
    /// Subtracts another <see cref="GpuBigFloat"/> from this instance.
    /// </summary>
    /// <param name="other">The value to subtract.</param>
    /// <returns>The difference of this instance minus <paramref name="other"/>.</returns>
    public GpuBigFloat Subtract(GpuBigFloat other) => this - other;

    /// <summary>
    /// Multiplies this instance by another <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <param name="other">The multiplier value.</param>
    /// <returns>The product of this instance and <paramref name="other"/>.</returns>
    public GpuBigFloat Multiply(GpuBigFloat other) => this * other;

    /// <summary>
    /// Divides this instance by another <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <param name="other">The divisor value.</param>
    /// <returns>The quotient of this instance divided by <paramref name="other"/>.</returns>
    public GpuBigFloat Divide(GpuBigFloat other) => this / other;

    /// <summary>
    /// Compares this instance to another <see cref="GpuBigFloat"/> and returns an integer indicating their relative order.
    /// </summary>
    /// <param name="other">The <see cref="GpuBigFloat"/> to compare with this instance.</param>
    /// <returns>
    /// A signed integer that indicates the relative values of this instance and <paramref name="other"/>:
    /// Less than zero if this instance is less than <paramref name="other"/>,
    /// Zero if this instance equals <paramref name="other"/>,
    /// Greater than zero if this instance is greater than <paramref name="other"/>.
    /// </returns>
    public int CompareTo(GpuBigFloat other)
    {
        var thisZero = UFloat.Significand.IsZero();
        var otherZero = other.UFloat.Significand.IsZero();

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

        using GpuBigFloat diff = this - other;
        if (diff.UFloat.Significand.IsZero())
        {
            return 0;
        }

        return diff.IsNegative ? -1 : 1;
    }

    /// <summary>
    /// Indicates whether the current instance is equal to another <see cref="GpuBigFloat"/> instance.
    /// </summary>
    /// <param name="other">A <see cref="GpuBigFloat"/> to compare with this instance.</param>
    /// <returns><see langword="true"/> if the current instance is equal to the <paramref name="other"/> parameter; otherwise, <see langword="false"/>.</returns>
    public bool Equals(GpuBigFloat other) => CompareTo(other) == 0;

    /// <summary>
    /// Determines whether the specified object is equal to the current instance.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="GpuBigFloat"/> and equals the current instance; otherwise, <see langword="false"/>.</returns>
    public override bool Equals(object? obj) => obj is GpuBigFloat other && Equals(other);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(UFloat, IsNegative);

    /// <summary>
    /// Determines whether two specified <see cref="GpuBigFloat"/> instances are equal.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> equals <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(GpuBigFloat left, GpuBigFloat right) => left.Equals(right);

    /// <summary>
    /// Determines whether two specified <see cref="GpuBigFloat"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not equal <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(GpuBigFloat left, GpuBigFloat right) => !left.Equals(right);

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigFloat"/> is less than another specified <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <(GpuBigFloat left, GpuBigFloat right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigFloat"/> is greater than another specified <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >(GpuBigFloat left, GpuBigFloat right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Implicitly evaluates and converts an addition expression to an evaluated <see cref="GpuBigFloat"/> instance.
    /// </summary>
    /// <param name="expr">The binary addition expression.</param>
    /// <returns>The evaluated <see cref="GpuBigFloat"/> result.</returns>
    public static implicit operator GpuBigFloat(BinaryExpr<GpuBigFloat, AddOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.UFloat.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    /// <summary>
    /// Implicitly evaluates and converts a subtraction expression to an evaluated <see cref="GpuBigFloat"/> instance.
    /// </summary>
    /// <param name="expr">The binary subtraction expression.</param>
    /// <returns>The evaluated <see cref="GpuBigFloat"/> result.</returns>
    public static implicit operator GpuBigFloat(BinaryExpr<GpuBigFloat, SubOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.UFloat.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    /// <summary>
    /// Implicitly evaluates and converts a multiplication expression to an evaluated <see cref="GpuBigFloat"/> instance.
    /// </summary>
    /// <param name="expr">The binary multiplication expression.</param>
    /// <returns>The evaluated <see cref="GpuBigFloat"/> result.</returns>
    public static implicit operator GpuBigFloat(BinaryExpr<GpuBigFloat, MulOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.UFloat.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    /// <summary>
    /// Implicitly evaluates and converts a division expression to an evaluated <see cref="GpuBigFloat"/> instance.
    /// </summary>
    /// <param name="expr">The binary division expression.</param>
    /// <returns>The evaluated <see cref="GpuBigFloat"/> result.</returns>
    public static implicit operator GpuBigFloat(BinaryExpr<GpuBigFloat, DivOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.UFloat.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigFloat"/> is less than or equal to another specified <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <=(GpuBigFloat left, GpuBigFloat right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigFloat"/> is greater than or equal to another specified <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >=(GpuBigFloat left, GpuBigFloat right) => left.CompareTo(right) >= 0;
}


