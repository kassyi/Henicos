
namespace Kassyi.GSharp.Stdlib.Math.Expressions;

/// <summary>
/// A value type wrapper that builds a lazy expression tree of GPU BigInteger arithmetic and bitwise operations.
/// </summary>
public readonly struct GpuBigIntExpr
{
    /// <summary>
    /// Gets the root AST node of this expression tree.
    /// </summary>
    public IGpuBigIntExpression Node
    {
        get;
    }

    internal GpuBigIntExpr(IGpuBigIntExpression node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
    }

    /// <summary>Implicitly wraps a <see cref="GpuBigInteger"/> buffer as an expression leaf node.</summary>
    public static implicit operator GpuBigIntExpr(GpuBigInteger b)
        => new(b);

    /// <summary>Constructs a lazy addition expression.</summary>
    public static GpuBigIntExpr operator +(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new AddExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy subtraction expression.</summary>
    public static GpuBigIntExpr operator -(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SubExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy multiplication expression.</summary>
    public static GpuBigIntExpr operator *(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new MulExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy division expression.</summary>
    public static GpuBigIntExpr operator /(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new DivExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy square root expression.</summary>
    public static GpuBigIntExpr Sqrt(GpuBigIntExpr expr)
        => new(new SqrtExpression(expr.Node));

    /// <summary>Constructs a lazy bitwise AND expression.</summary>
    public static GpuBigIntExpr operator &(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new BitwiseAndExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy bitwise OR expression.</summary>
    public static GpuBigIntExpr operator |(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new BitwiseOrExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy bitwise XOR expression.</summary>
    public static GpuBigIntExpr operator ^(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new BitwiseXorExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy bitwise NOT expression.</summary>
    public static GpuBigIntExpr operator ~(GpuBigIntExpr left)
        => new(new BitwiseNotExpression(left.Node));

    /// <summary>Constructs a lazy left shift expression.</summary>
    public static GpuBigIntExpr operator <<(GpuBigIntExpr left, int shiftBits)
        => new(new ShiftLeftExpression(left.Node, shiftBits));

    /// <summary>Constructs a lazy right shift expression.</summary>
    public static GpuBigIntExpr operator >>(GpuBigIntExpr left, int shiftBits)
        => new(new ShiftRightExpression(left.Node, shiftBits));
}

