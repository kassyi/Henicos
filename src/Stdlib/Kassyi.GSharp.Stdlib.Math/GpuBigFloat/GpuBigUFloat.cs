using Kassyi.GSharp.Stdlib.Math.GpuBigFloat.Expressions;
using Kassyi.GSharp.Stdlib.Math.GpuBigFloat.Runtime;
using Kassyi.GSharp.Stdlib.Math.GpuBigInt.Expressions;
using Kassyi.GSharp.Stdlib.Math.GpuBigInt.Runtime;

namespace Kassyi.GSharp.Stdlib.Math.GpuBigFloat;

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
    /// Disposes the underlying GPU memory.
    /// </summary>
    public void Dispose() => Significand.Dispose();

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

    public static BinaryExpr<GpuBigUFloat, AddOp> operator +(GpuBigUFloat left, GpuBigUFloat right) =>
        new(left, right);

    public static BinaryExpr<GpuBigUFloat, SubOp> operator -(GpuBigUFloat left, GpuBigUFloat right) =>
        new(left, right);

    public (GpuBigUFloat AlignedThis, GpuBigUFloat AlignedOther, bool Guard, bool Round, bool StickyThis, bool
        StickyOther) AlignRadixPoint(GpuBigUFloat other) => throw new NotImplementedException();

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

    public (GpuBigUFloat Quotient, uint Remainder) DivideByUInt32(uint divisor)
    {
        var resultSig = new GpuUIntBigInteger(Significand.Workspace, Significand.NumWords);
        var execContext = new GpuBigIntExecutionContext(
            Significand.Workspace,
            Significand.Workspace.MemoryManager,
            new LegacyProviderArithmeticAdapter(),
            Significand.Workspace.Stream);

        IGpuBigFloatArithmetic arithmetic = Significand.Workspace.Engine == GpuExecutionEngine.GSharpAot
            ? Providers.GSharpAotFloatProvider.Instance
            : Providers.CSharpCpuBigFloatArithmetic.Instance;

        arithmetic.DivideByUInt32(ref execContext,
            new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(Significand.DevicePointer, Significand.NumWords), 0,
                Significand.NumWords), Exponent, false,
            divisor,
            new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0,
                resultSig.NumWords), out var resultExp, out var _, out var remainder);

        return (new GpuBigUFloat(resultSig, resultExp), remainder);
    }

    public GpuBigUFloat Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit) =>
        throw new NotImplementedException();

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

    public static implicit operator GpuBigUFloat(BinaryExpr<GpuBigUFloat, AddOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    public static implicit operator GpuBigUFloat(BinaryExpr<GpuBigUFloat, SubOp> expr)
    {
        var visitor = new GpuBigFloatExpressionVisitor(expr.Left.Significand.Workspace);
        return expr.Evaluate(ref visitor);
    }

    public static bool operator <=(GpuBigUFloat left, GpuBigUFloat right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuBigUFloat left, GpuBigUFloat right) => left.CompareTo(right) >= 0;
}


