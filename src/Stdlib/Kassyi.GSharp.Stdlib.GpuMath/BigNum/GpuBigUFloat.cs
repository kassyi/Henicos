using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;


namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents an arbitrary-precision unsigned floating-point number on the GPU using a dynamic-size significand and separate exponent.
/// </summary>
/// <remarks>
/// <para><b>Representation:</b> Value equals $\text{Significand} \times 2^{\text{Exponent}}$.</para>
/// <para><b>Performance:</b> Backed by device memory allocations in <see cref="GpuBigUInt"/> significand with zero heap overhead on the host.</para>
/// <para><b>Memory Management:</b> Implements <see cref="IDisposable"/> to release the GPU buffer allocated for <see cref="Significand"/>.</para>
/// <para><b>Thread Safety:</b> Value-type semantics; distinct instances are safe for concurrent read operations.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuBigUFloat : IEquatable<GpuBigUFloat>, IComparable<GpuBigUFloat>, IDisposable
{
    /// <summary>
    /// Gets the significand (mantissa) stored as a dynamic-size unsigned big integer in GPU device memory.
    /// </summary>
    public GpuBigUInt Significand { get; }

    /// <summary>
    /// Gets the binary base-2 exponent of the floating-point number.
    /// </summary>
    public int Exponent { get; }

    /// <summary>
    /// Releases the GPU global device memory resources allocated for the underlying <see cref="Significand"/>.
    /// </summary>
    public void Dispose() => Significand.Dispose();

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigUFloat"/> struct with the specified significand and exponent.
    /// </summary>
    /// <param name="significand">The significand payload. Takes ownership of the GPU buffer.</param>
    /// <param name="exponent">The binary base-2 exponent.</param>
    public GpuBigUFloat([TakesOwnership] GpuBigUInt significand, int exponent)
    {
        Significand = significand;
        Exponent = exponent;
    }

    /// <summary>
    /// Creates a deferred expression representing the addition of two unsigned floating-point values.
    /// </summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <returns>A binary expression node representing the addition.</returns>
    public static BinaryExpr<GpuBigUFloat, AddOp> operator +(GpuBigUFloat left, GpuBigUFloat right) =>
        new(left, right);

    /// <summary>
    /// Creates a deferred expression representing the subtraction of two unsigned floating-point values.
    /// </summary>
    /// <param name="left">The minuend operand.</param>
    /// <param name="right">The subtrahend operand.</param>
    /// <returns>A binary expression node representing the subtraction.</returns>
    public static BinaryExpr<GpuBigUFloat, SubOp> operator -(GpuBigUFloat left, GpuBigUFloat right) =>
        new(left, right);

    /// <summary>
    /// Aligns the radix points (exponents) of this instance and another unsigned floating-point number, extracting IEEE 754 rounding bits.
    /// </summary>
    /// <param name="other">The other unsigned floating-point instance to align with.</param>
    /// <returns>A tuple containing the aligned values and IEEE 754 guard, round, and sticky bits.</returns>
    public (GpuBigUFloat AlignedThis, GpuBigUFloat AlignedOther, bool Guard, bool Round, bool StickyThis, bool
        StickyOther) AlignRadixPoint(GpuBigUFloat other) => throw new NotImplementedException();

    /// <summary>
    /// Normalizes the unsigned floating-point value by shifting significand bits and adjusting the exponent.
    /// </summary>
    /// <param name="leadingZeros">The number of leading zero bits in the significand.</param>
    /// <param name="carryOut"><see langword="true"/> if an arithmetic carry out occurred during addition; otherwise, <see langword="false"/>.</param>
    /// <returns>The normalized <see cref="GpuBigUFloat"/> instance.</returns>
    public GpuBigUFloat Normalize(int leadingZeros = 0, bool carryOut = false)
    {
        if (Significand.IsZero() && !carryOut)
        {
            return new GpuBigUFloat(Significand, 0);
        }

        var normExp = Exponent;
        var normSig = Significand;

        if (carryOut)
        {
            var expr = normSig >> 1;
            normSig = expr.Node.Evaluate(normSig.Workspace);
            checked
            {
                normExp = Exponent + 1;
            }
        }
        else if (leadingZeros > 0)
        {
            var expr = normSig << leadingZeros;
            normSig = expr.Node.Evaluate(normSig.Workspace);
            checked
            {
                normExp = Exponent - leadingZeros;
            }
        }

        return new GpuBigUFloat(normSig, normExp);
    }

    /// <summary>
    /// Divides the unsigned floating-point number by a 32-bit unsigned integer scalar.
    /// </summary>
    /// <param name="divisor">The 32-bit unsigned integer divisor.</param>
    /// <returns>A tuple containing the quotient <see cref="GpuBigUFloat"/> and the 32-bit integer remainder.</returns>
    public (GpuBigUFloat Quotient, uint Remainder) DivideByUInt32(uint divisor)
    {
        var resultSig = new GpuBigUInt(Significand.Workspace, Significand.NumWords);
        var execContext = new GpuBigIntExecutionContext(
            Significand.Workspace,
            Significand.Workspace.MemoryManager,
            new LegacyProviderArithmeticAdapter(),
            Significand.Workspace.Stream);

        IGpuBigFloatArithmetic arithmetic = Significand.Workspace.Engine == GpuExecutionEngine.GSharpAot
            ? (Significand.Workspace.Provider is NativeProvider
                ? GSharpAotFloatProvider.CreateNativeCuda()
                : GSharpAotFloatProvider.CreateGSharpKernel())
            : CSharpCpuBigFloatArithmetic.Instance;

        arithmetic.DivideByUInt32(ref execContext,
            new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(Significand.DevicePointer, Significand.NumWords), 0,
                Significand.NumWords), Exponent, false,
            divisor,
            new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0,
                resultSig.NumWords), out var resultExp, out var _, out var remainder);

        return (new GpuBigUFloat(resultSig, resultExp), remainder);
    }

    /// <summary>
    /// Rounds the floating-point value to the target significand precision using the specified rounding mode and discarded bit flags.
    /// </summary>
    /// <param name="mode">The IEEE 754 rounding mode to apply.</param>
    /// <param name="guardBit">The first discarded bit beyond the target precision.</param>
    /// <param name="roundBit">The second discarded bit beyond the target precision.</param>
    /// <param name="stickyBit"><see langword="true"/> if any remaining discarded bits beyond <paramref name="roundBit"/> were non-zero.</param>
    /// <returns>The rounded <see cref="GpuBigUFloat"/> instance.</returns>
    public GpuBigUFloat Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit) =>
        throw new NotImplementedException();

    /// <summary>
    /// Adds another <see cref="GpuBigUFloat"/> to this instance.
    /// </summary>
    /// <param name="other">The value to add.</param>
    /// <returns>The sum of this instance and <paramref name="other"/>.</returns>
    public GpuBigUFloat Add(GpuBigUFloat other) => throw new NotImplementedException();

    /// <summary>
    /// Subtracts another <see cref="GpuBigUFloat"/> from this instance.
    /// </summary>
    /// <param name="other">The value to subtract.</param>
    /// <returns>The difference of this instance minus <paramref name="other"/>.</returns>
    public GpuBigUFloat Subtract(GpuBigUFloat other) => throw new NotImplementedException();

    /// <summary>
    /// Compares this instance to another <see cref="GpuBigUFloat"/> and returns an integer indicating their relative order.
    /// </summary>
    /// <param name="other">The <see cref="GpuBigUFloat"/> to compare with this instance.</param>
    /// <returns>
    /// A signed integer that indicates the relative values of this instance and <paramref name="other"/>:
    /// Less than zero if this instance is less than <paramref name="other"/>,
    /// Zero if this instance equals <paramref name="other"/>,
    /// Greater than zero if this instance is greater than <paramref name="other"/>.
    /// </returns>
    public int CompareTo(GpuBigUFloat other) => throw new NotImplementedException();

    /// <summary>
    /// Indicates whether the current instance is equal to another <see cref="GpuBigUFloat"/> instance.
    /// </summary>
    /// <param name="other">A <see cref="GpuBigUFloat"/> to compare with this instance.</param>
    /// <returns><see langword="true"/> if the current instance is equal to the <paramref name="other"/> parameter; otherwise, <see langword="false"/>.</returns>
    public bool Equals(GpuBigUFloat other) => throw new NotImplementedException();

    /// <summary>
    /// Determines whether the specified object is equal to the current instance.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="GpuBigUFloat"/> and equals the current instance; otherwise, <see langword="false"/>.</returns>
    public override bool Equals(object? obj) => obj is GpuBigUFloat other && Equals(other);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Significand, Exponent);

    /// <summary>
    /// Determines whether two specified <see cref="GpuBigUFloat"/> instances are equal.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> equals <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(GpuBigUFloat left, GpuBigUFloat right) => left.Equals(right);

    /// <summary>
    /// Determines whether two specified <see cref="GpuBigUFloat"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not equal <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(GpuBigUFloat left, GpuBigUFloat right) => !left.Equals(right);

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigUFloat"/> is less than another specified <see cref="GpuBigUFloat"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <(GpuBigUFloat left, GpuBigUFloat right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigUFloat"/> is greater than another specified <see cref="GpuBigUFloat"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >(GpuBigUFloat left, GpuBigUFloat right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Implicitly evaluates and converts an addition expression to an evaluated <see cref="GpuBigUFloat"/> instance.
    /// </summary>
    /// <param name="expr">The binary addition expression.</param>
    /// <returns>The evaluated <see cref="GpuBigUFloat"/> result.</returns>
    public static implicit operator GpuBigUFloat(BinaryExpr<GpuBigUFloat, AddOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    /// <summary>
    /// Implicitly evaluates and converts a subtraction expression to an evaluated <see cref="GpuBigUFloat"/> instance.
    /// </summary>
    /// <param name="expr">The binary subtraction expression.</param>
    /// <returns>The evaluated <see cref="GpuBigUFloat"/> result.</returns>
    public static implicit operator GpuBigUFloat(BinaryExpr<GpuBigUFloat, SubOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigUFloat"/> is less than or equal to another specified <see cref="GpuBigUFloat"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <=(GpuBigUFloat left, GpuBigUFloat right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigUFloat"/> is greater than or equal to another specified <see cref="GpuBigUFloat"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >=(GpuBigUFloat left, GpuBigUFloat right) => left.CompareTo(right) >= 0;
}


