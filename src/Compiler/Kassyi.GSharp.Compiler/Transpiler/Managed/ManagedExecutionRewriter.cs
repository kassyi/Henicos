namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Rewrites lowered device function calls and state machine constructs into managed execution equivalents.
/// </summary>
internal sealed partial class ManagedExecutionRewriter(SemanticModel semanticModel, bool isDeviceFunction)
    : CSharpSyntaxRewriter
{
    private int _inLambdaOrLocalFuncCount;

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
        VisitCreation(node, base.VisitObjectCreationExpression(node));

    public override SyntaxNode?
        VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
        VisitCreation(node, base.VisitImplicitObjectCreationExpression(node));

    private SyntaxNode? VisitCreation(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited) =>
        visited is BaseObjectCreationExpressionSyntax rewritten &&
        semanticModel.GetTypeSymbol(node).IsSharedMemoryType()
            ? ManagedCSharpTranspiler.AddSharedMemorySiteKey(node, rewritten, semanticModel)
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
