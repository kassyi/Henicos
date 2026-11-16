namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>An expression: a node with a type, a value category and possibly a constant value.</summary>
/// <param name="type">The type of the value.</param>
/// <param name="category">How the node that holds the expression uses it.</param>
/// <param name="constantValue">The C# constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal abstract class IrExpression(ITypeSymbol type, IrCategory category, Optional<object?> constantValue,
    IrSource source) : IrNode(source)
{
    /// <summary>The type of the value; conversions are explicit <see cref="Expressions.IrConversion"/> nodes.</summary>
    public ITypeSymbol Type { get; } = type;

    /// <summary>How the node that holds the expression uses it.</summary>
    public IrCategory Category { get; } = category;

    /// <summary>
    /// The C# constant value. The builder keeps the structure of a constant expression (<c>1 + 2</c> stays a binary
    /// node); whether to fold it is decided later.
    /// </summary>
    public Optional<object?> ConstantValue { get; } = constantValue;

    /// <summary>
    /// The parentheses that directly enclosed the expression in the source: 2 for <c>((a))</c>; 0 for an expression
    /// without them, an implicit one, or one a pass made. Spelling only: the writers add the parentheses the precedence
    /// needs whatever this says.
    /// </summary>
    public int Parentheses { get; private set; }

    /// <summary>
    /// <paramref name="rebuilt"/>, which an <c>Update</c> or <c>With...</c> of this node has just created, with the
    /// spelling of this node.
    /// </summary>
    protected T KeepSpelling<T>(T rebuilt) where T : IrExpression
    {
        IrExpression node = rebuilt; // [WHY] the private setter is reached through the base type
        node.Parentheses = Parentheses;
        return rebuilt;
    }

    /// <summary>This node with <paramref name="count"/> parentheses; this node itself when it has that many.</summary>
    public IrExpression WithParentheses(int count)
    {
        if (count == Parentheses)
        {
            return this;
        }

        // [WHY] Every other field is read-only, so the copy shares nothing that changes
        var copy = (IrExpression)MemberwiseClone();
        copy.Parentheses = count;
        return copy;
    }

    /// <summary><see cref="WithParentheses(int)"/> that keeps the type of <paramref name="node"/>.</summary>
    internal static T WithParentheses<T>(T node, int count) where T : IrExpression =>
        (T)node.WithParentheses(count);
}
