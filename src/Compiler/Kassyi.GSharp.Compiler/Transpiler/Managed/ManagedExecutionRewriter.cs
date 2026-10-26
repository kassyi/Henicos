namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Rewrites lowered device function calls and state machine constructs into managed execution equivalents.
/// </summary>
internal sealed partial class ManagedExecutionRewriter(SemanticModel semanticModel, bool isDeviceFunction)
    : CSharpSyntaxRewriter
{
    private int _inLambdaOrLocalFuncCount;

    // The number of the next loop variable that a Gpu.ForUnroll becomes; found on the first visit, which is the body
    private int _nextUnrollId = -1;

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        if (_nextUnrollId < 0 && node is not null)
        {
            _nextUnrollId = FindNextUnrollId(node);
        }

        return base.Visit(node);
    }

    // A loop that PreLoweringExpander made of a barrier-bearing ForUnroll already uses these names. Its variable lives
    // in the context, so the two never clash; counting on from it only keeps the variables apart in the generated code
    private static int FindNextUnrollId(SyntaxNode root)
    {
        var next = 0;
        foreach (var token in root.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) &&
                token.ValueText.StartsWith(GSharpInternalNames.Lowering.UnrollIndexPrefix, StringComparison.Ordinal) &&
                int.TryParse(
                    token.ValueText[GSharpInternalNames.Lowering.UnrollIndexPrefix.Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var id) &&
                id >= next)
            {
                next = id + 1;
            }
        }

        return next;
    }

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
        VisitCreation(node, base.VisitObjectCreationExpression(node));

    public override SyntaxNode?
        VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
        VisitCreation(node, base.VisitImplicitObjectCreationExpression(node));

    private SyntaxNode? VisitCreation(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited) =>
        visited is BaseObjectCreationExpressionSyntax rewritten &&
        semanticModel.GetTypeSymbol(node).IsSharedMemoryType()
            ? SharedMemorySiteKeyAnnotator.AddSharedMemorySiteKey(node, rewritten, semanticModel)
            : visited;

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var visited = base.VisitMemberAccessExpression(node);
        if (!ContextLocalRewriter.IsContextPointerAccess(visited) ||
            visited is not MemberAccessExpressionSyntax memberAccess)
        {
            return visited;
        }

        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            memberAccess.Expression,
            memberAccess.Name).WithTriviaFrom(memberAccess);
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node)
        {
            return base.VisitIdentifierName(node);
        }

        return node.Identifier.Text switch
        {
            // The managed shuffle helpers keep the warp exchange themselves; any context will do
            GSharpInternalNames.SuspendingCall.Root =>
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context).WithTriviaFrom(node),
            // __gsharp_ctx.<name>
            GSharpInternalNames.StateMachine.State
                or GSharpInternalNames.StateMachine.WarpExchange
                or GSharpInternalNames.StateMachine.YieldedForGridSync =>
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
                    node
                ).WithTriviaFrom(node),
            // System.Numerics.BitOperations
            TypeConstants.BitOperationsType =>
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("System.Numerics"),
                    SyntaxFactory.IdentifierName(TypeConstants.BitOperationsType)
                ).WithTriviaFrom(node),
            _ => base.VisitIdentifierName(node)
        };
    }

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) =>
        VisitNestedCallable(node, base.VisitLocalFunctionStatement);

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) =>
        VisitNestedCallable(node, base.VisitSimpleLambdaExpression);

    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) =>
        VisitNestedCallable(node, base.VisitParenthesizedLambdaExpression);

    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) =>
        VisitNestedCallable(node, base.VisitAnonymousMethodExpression);

    private SyntaxNode? VisitNestedCallable<TNode>(TNode node, Func<TNode, SyntaxNode?> baseVisitor)
    {
        _inLambdaOrLocalFuncCount++;
        try
        {
            return baseVisitor(node);
        }
        finally
        {
            _inLambdaOrLocalFuncCount--;
        }
    }
}
