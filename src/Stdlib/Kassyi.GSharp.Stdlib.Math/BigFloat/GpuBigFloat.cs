using Kassyi.GSharp.Stdlib.Math.BigFloat.Expressions;

namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

/// <summary>
/// Represents an arbitrary-precision signed floating-point number on the GPU using an unsigned floating-point value and a sign flag.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuBigFloat : IEquatable<GpuBigFloat>, IComparable<GpuBigFloat>, IDisposable
{
    /// <summary>
    /// The underlying unsigned floating-point value payload.
    /// </summary>
    public GpuBigUFloat UFloat { get; }

    /// <summary>
    /// Gets a value indicating whether the floating-point number is negative.
    /// </summary>
    public bool IsNegative { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigFloat"/> struct.
    /// </summary>
    /// <param name="uFloat">The unsigned float component.</param>
    /// <param name="isNegative"><see langword="true"/> if negative; otherwise, <see langword="false"/>.</param>
    public GpuBigFloat([TakesOwnership] GpuBigUFloat uFloat, bool isNegative)
    {
        UFloat = uFloat;
        IsNegative = isNegative;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigFloat"/> struct from significand, exponent, and sign flag.
    /// </summary>
    /// <param name="significand">The significand payload.</param>
    /// <param name="exponent">The binary exponent.</param>
    /// <param name="isNegative"><see langword="true"/> if negative; otherwise, <see langword="false"/>.</param>
    public GpuBigFloat([TakesOwnership] GpuUIntBigInteger significand, int exponent, bool isNegative)
        : this(new GpuBigUFloat(significand, exponent), isNegative)
    {
    }

    public void Dispose()
    {
        UFloat.Dispose();
    }

    public static GpuBigFloatAddExpr operator +(GpuBigFloat left, GpuBigFloat right) => new GpuBigFloatAddExpr(left, right);
    public static GpuBigFloatSubExpr operator -(GpuBigFloat left, GpuBigFloat right) => new GpuBigFloatSubExpr(left, right);
    public static GpuBigFloatMulExpr operator *(GpuBigFloat left, GpuBigFloat right) => new GpuBigFloatMulExpr(left, right);

    public (GpuBigFloat AlignedThis, GpuBigFloat AlignedOther, bool Guard, bool Round, bool StickyThis, bool StickyOther) AlignRadixPoint(GpuBigFloat other) => throw new NotImplementedException();
        public GpuBigFloat Normalize(int leadingZeros = 0, bool carryOut = false) =>
        new GpuBigFloat(UFloat.Normalize(leadingZeros, carryOut), IsNegative);

    public (GpuBigFloat Quotient, uint Remainder) DivideByUInt32(uint divisor)
    {
        var (uQuotient, remainder) = UFloat.DivideByUInt32(divisor);
        return (new GpuBigFloat(uQuotient, IsNegative), remainder);
    }
    public GpuBigFloat Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit) => throw new NotImplementedException();

    public GpuBigFloat Add(GpuBigFloat other) => throw new NotImplementedException();
    public GpuBigFloat Subtract(GpuBigFloat other) => throw new NotImplementedException();

    public int CompareTo(GpuBigFloat other) => throw new NotImplementedException();
    public bool Equals(GpuBigFloat other) => throw new NotImplementedException();
    public override bool Equals(object? obj) => obj is GpuBigFloat other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(UFloat, IsNegative);

    public static bool operator ==(GpuBigFloat left, GpuBigFloat right) => left.Equals(right);
    public static bool operator !=(GpuBigFloat left, GpuBigFloat right) => !left.Equals(right);
    public static bool operator <(GpuBigFloat left, GpuBigFloat right) => left.CompareTo(right) < 0;
    public static bool operator >(GpuBigFloat left, GpuBigFloat right) => left.CompareTo(right) > 0;
        public static implicit operator GpuBigFloat(GpuBigFloatAddExpr expr) { var visitor = new BigFloatExpressionVisitor(expr.Left.UFloat.Significand.Context); return expr.Accept(ref visitor); }
    public static implicit operator GpuBigFloat(GpuBigFloatSubExpr expr) { var visitor = new BigFloatExpressionVisitor(expr.Left.UFloat.Significand.Context); return expr.Accept(ref visitor); }
    public static implicit operator GpuBigFloat(GpuBigFloatMulExpr expr) { var visitor = new BigFloatExpressionVisitor(expr.Left.UFloat.Significand.Context); return expr.Accept(ref visitor); }
    public static bool operator <=(GpuBigFloat left, GpuBigFloat right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuBigFloat left, GpuBigFloat right) => left.CompareTo(right) >= 0;
}


