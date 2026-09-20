namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

/// <summary>
/// A value type wrapper that builds a lazy expression tree of GPU signed BigInteger arithmetic.
/// Deferred sign resolution allows for robust evaluation of operations where signs depend on
/// runtime magnitudes (e.g. addition of different signs).
/// </summary>
public readonly struct GpuBigIntExpr
{
    /// <summary>
    /// Gets the root AST node of this signed expression tree.
    /// </summary>
    public IGpuBigIntExpression Node
    {
        get;
    }

    internal GpuBigIntExpr(IGpuBigIntExpression node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
    }

    /// <summary>Implicitly wraps a <see cref="GpuBigInteger"/> as a signed expression leaf node.</summary>
    public static implicit operator GpuBigIntExpr(GpuBigInteger b)
        => new(new GpuBigIntLeafExpression(b));

    /// <summary>Constructs a lazy multiplication expression.</summary>
    public static GpuBigIntExpr operator *(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedMulExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy division expression.</summary>
    public static GpuBigIntExpr operator /(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedDivExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy addition expression with deferred sign resolution.</summary>
    public static GpuBigIntExpr operator +(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedAddExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy subtraction expression with deferred sign resolution.</summary>
    public static GpuBigIntExpr operator -(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedSubExpression(left.Node, right.Node));

    /// <summary>Constructs a lazy negation expression.</summary>
    public static GpuBigIntExpr operator -(GpuBigIntExpr left)
        => new(new SignedNegateExpression(left.Node));

    /// <summary>Constructs a lazy bitwise NOT expression.</summary>
    public static GpuBigIntExpr operator ~(GpuBigIntExpr left)
        => new(new SignedBitwiseNotExpression(left.Node));

    /// <summary>
    /// Evaluates this signed expression into the provided destination buffer.
    /// </summary>
    /// <param name="destination">The pre-allocated GPU buffer to store the evaluated magnitude.</param>
    /// <returns>A new <see cref="GpuBigInteger"/> instance wrapping the destination buffer with the evaluated sign.</returns>
    public GpuBigInteger Evaluate(GpuUIntBigInteger destination) => Node.Evaluate(destination.Context, destination);

    /// <summary>
    /// Evaluates this signed expression into a newly allocated temporary GPU buffer.
    /// </summary>
    /// <param name="context">The GPU context used for temporary buffer allocation.</param>
    /// <returns>A new <see cref="GpuBigInteger"/> instance wrapping the temporary buffer with the evaluated sign.</returns>
    public GpuBigInteger Evaluate(GpuContext context) => Node.Evaluate(context);
}
