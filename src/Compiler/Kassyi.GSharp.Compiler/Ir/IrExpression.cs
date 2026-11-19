using Kassyi.GSharp.Compiler.Ir.Decisions;

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

    private TextSpan? _spellingSpan;

    /// <summary>
    /// The span of the expression including any enclosing parentheses and null-forgiving operators (<c>!</c>); equals
    /// <see cref="IrNode.Source"/>'s span when none surround it.
    /// </summary>
    public TextSpan SpellingSpan => _spellingSpan ?? Source.Span;

    /// <summary>
    /// <see cref="IrNode.Source"/> with <see cref="SpellingSpan"/> as its span, for error sites that report the
    /// expression as written in the source.
    /// </summary>
    internal IrSource SpellingSource =>
        _spellingSpan is { } span ? Source with { Start = span.Start, Length = span.Length } : Source;

    /// <summary>
    /// Whether <see cref="SpellingSpan"/> holds a null-forgiving <c>!</c>, not only parentheses. A temporary that
    /// replaces the expression takes the parentheses written directly around it, and leaves the <c>!</c> and what
    /// encloses it in place.
    /// </summary>
    internal bool SpellingHasNullForgiving { get; private set; }

    /// <summary>
    /// What the decision pass decided; <see langword="null"/> before it ran. Read it through the typed property of
    /// the node.
    /// </summary>
    public IrDecision? Decision { get; private set; }

    /// <summary>
    /// The decision as <typeparamref name="T"/>; an error when the pass has not run or decided another kind.
    /// </summary>
    protected T GetDecision<T>() where T : IrDecision =>
        Decision as T ?? throw new InvalidOperationException(Decision is null
            ? $"{GetType().Name} has not been decided; run the decision pass first."
            : $"{GetType().Name} holds a {Decision.GetType().Name}, not a {typeof(T).Name}.");

    /// <summary>
    /// A copy of <paramref name="node"/> that holds <paramref name="decision"/>; <paramref name="node"/> itself when
    /// it holds that one.
    /// </summary>
    internal static TNode WithDecision<TNode>(TNode node, IrDecision decision) where TNode : IrExpression
    {
        if (ReferenceEquals(node.Decision, decision))
        {
            return node;
        }

        // [WHY] Every other field is read-only, so the copy shares nothing that changes
        var copy = (TNode)node.MemberwiseClone();
        ((IrExpression)copy).Decision = decision;
        return copy;
    }

    /// <summary>
    /// <paramref name="rebuilt"/>, which an <c>Update</c> or <c>With...</c> of this node has just created, with the
    /// spelling and the decision of this node. The decision stays valid only when children are replaced with values of
    /// the same type and range.
    /// </summary>
    protected T KeepSpelling<T>(T rebuilt) where T : IrExpression
    {
        IrExpression node = rebuilt; // [WHY] the private setter is reached through the base type
        node.Parentheses = Parentheses;
        node._spellingSpan = _spellingSpan;
        node.SpellingHasNullForgiving = SpellingHasNullForgiving;
        node.Decision = Decision;
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

    /// <summary>
    /// This node with <paramref name="parentheses"/>, <paramref name="spellingSpan"/> and
    /// <paramref name="nullForgiving"/> (whether the spelling holds a <c>!</c>); this node itself when all already
    /// match.
    /// </summary>
    public IrExpression WithSpelling(int parentheses, TextSpan? spellingSpan, bool nullForgiving = false)
    {
        var normalized = spellingSpan == Source.Span ? null : spellingSpan;
        var forgiving = normalized is not null && nullForgiving;
        if (parentheses == Parentheses && normalized == _spellingSpan && forgiving == SpellingHasNullForgiving)
        {
            return this;
        }

        // [WHY] Every other field is read-only, so the copy shares nothing that changes
        var copy = (IrExpression)MemberwiseClone();
        copy.Parentheses = parentheses;
        copy._spellingSpan = normalized;
        copy.SpellingHasNullForgiving = forgiving;
        return copy;
    }

    /// <summary>
    /// <see cref="WithSpelling(int, TextSpan?, bool)"/> that keeps the type of <paramref name="node"/>.
    /// </summary>
    internal static T WithSpelling<T>(T node, int parentheses, TextSpan? spellingSpan, bool nullForgiving = false)
        where T : IrExpression =>
        (T)node.WithSpelling(parentheses, spellingSpan, nullForgiving);
}
