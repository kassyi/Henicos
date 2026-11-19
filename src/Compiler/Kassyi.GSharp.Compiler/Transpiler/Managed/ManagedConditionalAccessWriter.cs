namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Writes the managed copy of the null-conditional accesses that need the pattern variable of
/// <see cref="ManagedConditionalAccess"/>, for a rewriter that visits the rest of the body (the managed rewriter, and
/// the name qualifier, so that both write the same form): the value form, the statement form, and the block body of a
/// void method, local function or lambda whose expression body is such an access.
/// </summary>
/// <param name="semanticModel">The model of the user's tree that the body comes from.</param>
/// <param name="visit">The visitor of the rewriter, for the receiver and <c>WhenNotNull</c>.</param>
internal sealed class ManagedConditionalAccessWriter(SemanticModel semanticModel, Func<SyntaxNode?, SyntaxNode?> visit)
{
    private int _nextReceiverId;

    // The access that is the expression body of the callable being visited, and the statement written for it
    private ConditionalAccessExpressionSyntax? _bodyAccess;
    private StatementSyntax? _bodyStatement;

    /// <summary>
    /// <c>(a is { } t ? (R)(global::N.C.M(t[i])) : default(R))</c> for <paramref name="node"/>, or
    /// <see langword="null"/> when it stays a null-conditional access (the caller visits it as usual).
    /// </summary>
    public SyntaxNode? TryVisitAccess(ConditionalAccessExpressionSyntax node)
    {
        if (!ManagedConditionalAccess.NeedsReceiverVariable(node, semanticModel))
        {
            return null;
        }

        // The expression body of a void callable: its statement goes into the block body of VisitCallable
        if (node == _bodyAccess)
        {
            _bodyStatement = WriteStatement(node);
            return node;
        }

        var receiver = VisitReceiver(node);
        var whenNotNull = VisitWhenNotNull(node);
        return ManagedConditionalAccess.CreateValue(node, receiver, whenNotNull, NextReceiverName(), semanticModel)
            .WithTriviaFrom(node);
    }

    /// <summary>
    /// <c>if (a is { } t) { global::N.C.M(t[i]); }</c> for an access used as a statement, or <see langword="null"/>.
    /// </summary>
    public StatementSyntax? TryVisitStatement(ExpressionStatementSyntax node) =>
        node.Expression is ConditionalAccessExpressionSyntax access &&
        ManagedConditionalAccess.NeedsReceiverVariable(access, semanticModel)
            ? WriteStatement(access).WithTriviaFrom(node)
            : null;

    /// <summary>
    /// The block body of a void method whose expression body is an access that needs the variable, or
    /// <see langword="null"/>.
    /// </summary>
    public BlockSyntax? TryWriteBody(ArrowExpressionClauseSyntax body) =>
        ManagedConditionalAccess.GetVoidBodyAccess(body, semanticModel) is { } access
            ? SyntaxFactory.Block(WriteStatement(access))
            : null;

    /// <summary>
    /// <paramref name="node"/> (a local function or a lambda) visited by <paramref name="visitCallable"/>, with a block
    /// body when its expression body is a void access that needs the variable.
    /// </summary>
    public SyntaxNode? VisitCallable(SyntaxNode node, Func<SyntaxNode, SyntaxNode?> visitCallable)
    {
        if (ManagedConditionalAccess.GetVoidBodyAccess(node, semanticModel) is not { } access)
        {
            return visitCallable(node);
        }

        var outerAccess = _bodyAccess;
        _bodyAccess = access;
        try
        {
            var visited = visitCallable(node);
            var statement = _bodyStatement ??
                            throw new InvalidOperationException($"The body of '{node}' did not rewrite.");
            return WithBlockBody(visited, SyntaxFactory.Block(statement));
        }
        finally
        {
            _bodyAccess = outerAccess;
            _bodyStatement = null;
        }
    }

    // The visited local function or lambda with block in place of its expression body
    private static SyntaxNode WithBlockBody(SyntaxNode? visited, BlockSyntax block) =>
        visited switch
        {
            LocalFunctionStatementSyntax function => function
                .WithExpressionBody(null)
                .WithSemicolonToken(default)
                .WithBody(block),
            AnonymousFunctionExpressionSyntax lambda => lambda.WithExpressionBody(null).WithBlock(block),
            _ => throw new InvalidOperationException($"'{visited}' did not rewrite to a callable.")
        };

    // A WhenNotNull that is an access needing its own variable is a statement as well (h?.Data?.M() becomes
    // if (h is { } t0) { if (t0.Data is { } t1) { M(t1); } }): a call that returns void has no value to write
    private StatementSyntax WriteStatement(ConditionalAccessExpressionSyntax node)
    {
        var receiver = VisitReceiver(node);
        var body = node.WhenNotNull is ConditionalAccessExpressionSyntax nested &&
                   ManagedConditionalAccess.NeedsReceiverVariable(nested, semanticModel)
            ? WriteStatement(nested)
            : SyntaxFactory.ExpressionStatement(VisitWhenNotNull(node));
        return ManagedConditionalAccess.CreateStatement(receiver, body, NextReceiverName());
    }

    // The receiver first and then WhenNotNull, in the order C# evaluates them
    private ExpressionSyntax VisitReceiver(ConditionalAccessExpressionSyntax node) =>
        visit(node.Expression) as ExpressionSyntax ??
        throw new InvalidOperationException($"The receiver of '{node}' did not rewrite.");

    private ExpressionSyntax VisitWhenNotNull(ConditionalAccessExpressionSyntax node) =>
        visit(node.WhenNotNull) as ExpressionSyntax ??
        throw new InvalidOperationException($"The access '{node}' did not rewrite.");

    private string NextReceiverName() =>
        GSharpInternalNames.Lowering.ConditionalReceiverPrefix + _nextReceiverId++;
}
