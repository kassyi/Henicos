
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

/// <summary>
/// A value type wrapper that builds a lazy expression tree of GPU BigInteger arithmetic and bitwise operations.
/// </summary>
public readonly struct GpuBigUIntExpr
{
    /// <summary>
    /// Gets the root AST node of this expression tree.
    /// </summary>
    public IGpuBigUIntExpression Node
    {
        get;
    }

    internal GpuBigUIntExpr(IGpuBigUIntExpression node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
    }

    /// <summary>Implicitly wraps a <see cref="GpuUIntBigInteger"/> buffer as an expression leaf node.</summary>
    public static implicit operator GpuBigUIntExpr(GpuUIntBigInteger b)
        => new(b);

    /// <summary>Constructs a lazy addition expression.</summary>
    public static GpuBigUIntExpr operator +(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new AddExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy subtraction expression.</summary>
    public static GpuBigUIntExpr operator -(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new SubExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy multiplication expression.</summary>
    public static GpuBigUIntExpr operator *(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new MulExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy division expression.</summary>
    public static GpuBigUIntExpr operator /(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new DivExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy square root expression.</summary>
    public static GpuBigUIntExpr Sqrt(GpuBigUIntExpr expr)
        => new(new SqrtExpression(expr.Node));

    /// <summary>Constructs a lazy bitwise AND expression.</summary>
    public static GpuBigUIntExpr operator &(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new BitwiseAndExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy bitwise OR expression.</summary>
    public static GpuBigUIntExpr operator |(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new BitwiseOrExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy bitwise XOR expression.</summary>
    public static GpuBigUIntExpr operator ^(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new BitwiseXorExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy bitwise NOT expression.</summary>
    public static GpuBigUIntExpr operator ~(GpuBigUIntExpr left)
        => new(new BitwiseNotExpression(left.Node));

    /// <summary>Constructs a lazy left shift expression.</summary>
    public static GpuBigUIntExpr operator <<(GpuBigUIntExpr left, int shiftBits)
        => new(new ShiftLeftExpression(left.Node, shiftBits));

    /// <summary>Constructs a lazy right shift expression.</summary>
    public static GpuBigUIntExpr operator >>(GpuBigUIntExpr left, int shiftBits)
        => new(new ShiftRightExpression(left.Node, shiftBits));
}


