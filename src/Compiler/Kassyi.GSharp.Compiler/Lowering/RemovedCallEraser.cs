namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Removes the calls that C# removes (<see cref="RemovedCalls"/>), their arguments included, from a body that lowering
/// copies, before any pass sees them: no split point, spilled argument or barrier of a removed call is left behind.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] A removed call stands only where a void expression may (<see cref="RemovedCalls"/>), and each place has a form
/// that does nothing:
/// <list type="bullet">
///   <item><description>an expression statement becomes the empty statement <c>;</c></description></item>
///   <item><description>an initializer or incrementor of a <c>for</c> is left out of the list</description></item>
///   <item><description>
///     the expression body of a lambda or a local function becomes the empty block <c>{ }</c>
///   </description></item>
/// </list>
/// </para>
/// <para>
/// The nodes it writes carry the original-span annotation of the node they replace (the statement, or the removed call
/// for an empty body), so the passes after it report in the user's source; every other node is kept as it is. The split
/// annotations of the removed call and of the calls in its arguments go with it. The managed rewriter removes the calls
/// of the bodies it copies without lowering with the same helpers.
/// </para>
/// </remarks>
internal sealed class RemovedCallEraser : CSharpSyntaxRewriter
{
    private readonly SemanticModel _semanticModel;

    private RemovedCallEraser(SemanticModel semanticModel) => _semanticModel = semanticModel;

    /// <summary>
    /// <paramref name="body"/> without the calls that C# removes; <paramref name="body"/> itself without a semantic
    /// model, with which nothing is decided.
    /// </summary>
    /// <param name="body">A body annotated with its original spans, or a node of the compilation.</param>
    /// <param name="semanticModel">A model of the compilation.</param>
    public static BlockSyntax Erase(BlockSyntax body, SemanticModel? semanticModel) =>
        semanticModel == null
            ? body
            : new RemovedCallEraser(semanticModel).Visit(body) as BlockSyntax ?? body;

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) =>
        RemovedCalls.IsRemovedCall(node.Expression, _semanticModel)
            ? ToEmptyStatement(node)
            : base.VisitExpressionStatement(node);

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node) =>
        base.VisitForStatement(node) is ForStatementSyntax visited
            ? WithoutRemovedCalls(node, visited, _semanticModel)
            : null;

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) =>
        WithEmptyBodyIfRemoved(node, base.VisitSimpleLambdaExpression(node), _semanticModel);

    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) =>
        WithEmptyBodyIfRemoved(node, base.VisitParenthesizedLambdaExpression(node), _semanticModel);

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) =>
        WithEmptyBodyIfRemoved(node, base.VisitLocalFunctionStatement(node), _semanticModel);

    /// <summary>
    /// The empty statement <c>;</c> in place of <paramref name="statement"/>, a removed call, with its trivia and its
    /// original-span annotation.
    /// </summary>
    public static EmptyStatementSyntax ToEmptyStatement(ExpressionStatementSyntax statement) =>
        SyntaxFactory.EmptyStatement()
            .WithTriviaFrom(statement)
            .WithAdditionalAnnotations(statement.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));

    /// <summary>
    /// <paramref name="visited"/>, the rewritten <paramref name="original"/>, without the initializers and incrementors
    /// that are removed calls; the elements of both lists correspond one to one.
    /// </summary>
    public static ForStatementSyntax WithoutRemovedCalls(ForStatementSyntax original, ForStatementSyntax visited,
        SemanticModel semanticModel) =>
        visited
            .WithInitializers(WithoutRemovedCalls(original.Initializers, visited.Initializers, semanticModel))
            .WithIncrementors(WithoutRemovedCalls(original.Incrementors, visited.Incrementors, semanticModel));

    /// <summary>
    /// <paramref name="visited"/>, the rewritten <paramref name="original"/> (a lambda or a local function), with
    /// the empty block <c>{ }</c> as its body when its expression body is a removed call; otherwise
    /// <paramref name="visited"/>.
    /// </summary>
    public static SyntaxNode? WithEmptyBodyIfRemoved(SyntaxNode original, SyntaxNode? visited,
        SemanticModel semanticModel)
    {
        switch (original, visited)
        {
            case (AnonymousFunctionExpressionSyntax { ExpressionBody: { } call },
                AnonymousFunctionExpressionSyntax function) when RemovedCalls.IsRemovedCall(call, semanticModel):
                return function
                    .WithExpressionBody(null)
                    .WithBlock(CreateEmptyBody(call));
            case (LocalFunctionStatementSyntax { ExpressionBody.Expression: var call },
                LocalFunctionStatementSyntax function) when RemovedCalls.IsRemovedCall(call, semanticModel):
                return function
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(CreateEmptyBody(call));
            default:
                return visited;
        }
    }

    // { } with the original-span annotation of the removed call it replaces
    private static BlockSyntax CreateEmptyBody(SyntaxNode call) =>
        SyntaxFactory.Block()
            .WithAdditionalAnnotations(call.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));

    private static SeparatedSyntaxList<ExpressionSyntax> WithoutRemovedCalls(
        SeparatedSyntaxList<ExpressionSyntax> original, SeparatedSyntaxList<ExpressionSyntax> visited,
        SemanticModel semanticModel)
    {
        for (var i = original.Count - 1; i >= 0; i--)
        {
            if (RemovedCalls.IsRemovedCall(original[i], semanticModel))
            {
                visited = visited.RemoveAt(i);
            }
        }

        return visited;
    }
}
