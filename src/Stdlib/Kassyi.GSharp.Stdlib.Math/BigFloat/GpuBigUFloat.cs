using Kassyi.GSharp.Stdlib.Math.BigFloat.Expressions;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

/// <summary>
/// Represents an arbitrary-precision unsigned floating-point number on the GPU using a dynamic-size significand and separate exponent.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuBigUFloat : IEquatable<GpuBigUFloat>, IComparable<GpuBigUFloat>, IDisposable
{
    /// <summary>
    /// The significand (mantissa) stored as a dynamic-size unsigned big integer.
    /// </summary>
    public GpuUIntBigInteger Significand { get; }

    /// <summary>
    /// The binary exponent of the floating-point number.
    /// </summary>
    public int Exponent { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigUFloat"/> struct.
    /// </summary>
    /// <param name="significand">The significand payload.</param>
    /// <param name="exponent">The binary exponent.</param>
    public GpuBigUFloat([TakesOwnership] GpuUIntBigInteger significand, int exponent)
    {
        Significand = significand;
        Exponent = exponent;
    }

    public void Dispose()
    {
        Significand.Dispose();
    }

    public static GpuBigUFloatAddExpr operator +(GpuBigUFloat left, GpuBigUFloat right) => new GpuBigUFloatAddExpr(left, right);
    public static GpuBigUFloatSubExpr operator -(GpuBigUFloat left, GpuBigUFloat right) => new GpuBigUFloatSubExpr(left, right);

    public (GpuBigUFloat AlignedThis, GpuBigUFloat AlignedOther, bool Guard, bool Round, bool StickyThis, bool StickyOther) AlignRadixPoint(GpuBigUFloat other) => throw new NotImplementedException();
        public GpuBigUFloat Normalize(int leadingZeros = 0, bool carryOut = false)
    {
        if (Significand.IsZero() && !carryOut)
        {
            return new GpuBigUFloat(Significand, 0);
        }

        int normExp = Exponent;
        var normSig = Significand;

        if (carryOut)
        {
            var expr = normSig >> 1;
            normSig = expr.Node.Evaluate(normSig.Context);
            checked { normExp = Exponent + 1; }
        }
        else if (leadingZeros > 0)
        {
            var expr = normSig << leadingZeros;
            normSig = expr.Node.Evaluate(normSig.Context);
            checked { normExp = Exponent - leadingZeros; }
        }

        return new GpuBigUFloat(normSig, normExp);
    }
    public GpuBigUFloat Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit) => throw new NotImplementedException();

    public GpuBigUFloat Add(GpuBigUFloat other) => throw new NotImplementedException();
    public GpuBigUFloat Subtract(GpuBigUFloat other) => throw new NotImplementedException();

    public int CompareTo(GpuBigUFloat other) => throw new NotImplementedException();
    public bool Equals(GpuBigUFloat other) => throw new NotImplementedException();
    public override bool Equals(object? obj) => obj is GpuBigUFloat other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Significand, Exponent);

    public static bool operator ==(GpuBigUFloat left, GpuBigUFloat right) => left.Equals(right);
    public static bool operator !=(GpuBigUFloat left, GpuBigUFloat right) => !left.Equals(right);
    public static bool operator <(GpuBigUFloat left, GpuBigUFloat right) => left.CompareTo(right) < 0;
    public static bool operator >(GpuBigUFloat left, GpuBigUFloat right) => left.CompareTo(right) > 0;
        public static implicit operator GpuBigUFloat(GpuBigUFloatAddExpr expr) { var visitor = new BigFloatExpressionVisitor(expr.Left.Significand.Context); return expr.Accept(ref visitor); }
    public static implicit operator GpuBigUFloat(GpuBigUFloatSubExpr expr) { var visitor = new BigFloatExpressionVisitor(expr.Left.Significand.Context); return expr.Accept(ref visitor); }
    public static bool operator <=(GpuBigUFloat left, GpuBigUFloat right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuBigUFloat left, GpuBigUFloat right) => left.CompareTo(right) >= 0;
}


