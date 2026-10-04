using Kassyi.GSharp.Stdlib.Math.GpuBigFloat.Expressions;

namespace Kassyi.GSharp.Stdlib.Math.GpuBigFloat;

/// <summary>
/// Represents an arbitrary-precision signed floating-point number on the GPU using an unsigned floating-point value and a sign flag.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuBigFloat : IEquatable<GpuBigFloat>, IComparable<GpuBigFloat>, IDisposable
{
    /// <summary>
    /// The underlying unsigned floating-point value payload.
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
    /// Disposes the underlying GPU memory.
    /// </summary>
    public void Dispose() => UFloat.Dispose();

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

    public static BinaryExpr<GpuBigFloat, AddOp> operator +(GpuBigFloat left, GpuBigFloat right) => new(left, right);
    public static BinaryExpr<GpuBigFloat, SubOp> operator -(GpuBigFloat left, GpuBigFloat right) => new(left, right);
    public static BinaryExpr<GpuBigFloat, MulOp> operator *(GpuBigFloat left, GpuBigFloat right) => new(left, right);

    public (GpuBigFloat AlignedThis, GpuBigFloat AlignedOther, bool Guard, bool Round, bool StickyThis, bool StickyOther
        ) AlignRadixPoint(GpuBigFloat other) => throw new NotImplementedException();

    public GpuBigFloat Normalize(int leadingZeros = 0, bool carryOut = false) =>
        new(UFloat.Normalize(leadingZeros, carryOut), IsNegative);

    public (GpuBigFloat Quotient, uint Remainder) DivideByUInt32(uint divisor)
    {
        var (uQuotient, remainder) = UFloat.DivideByUInt32(divisor);
        return (new GpuBigFloat(uQuotient, IsNegative), remainder);
    }

    public GpuBigFloat Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit) =>
        throw new NotImplementedException();

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

    public static implicit operator GpuBigFloat(BinaryExpr<GpuBigFloat, AddOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.UFloat.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    public static implicit operator GpuBigFloat(BinaryExpr<GpuBigFloat, SubOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.UFloat.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    public static implicit operator GpuBigFloat(BinaryExpr<GpuBigFloat, MulOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.UFloat.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    public static bool operator <=(GpuBigFloat left, GpuBigFloat right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuBigFloat left, GpuBigFloat right) => left.CompareTo(right) >= 0;
}


