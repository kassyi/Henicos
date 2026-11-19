namespace Kassyi.GSharp.Compiler.Transpiler;

internal sealed partial class ManagedExecutionRewriter
{
    // a?[i].M() with an extension method M becomes (a is { } t ? (R)(global::N.C.M(t[i])) : default(R))
    // (ManagedConditionalAccess); any other access stays a null-conditional access
    public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
    {
        if (!ManagedConditionalAccess.NeedsReceiverVariable(node, semanticModel))
        {
            return base.VisitConditionalAccessExpression(node);
        }

        var (receiver, whenNotNull, variable) = VisitWithReceiverVariable(node);
        return ManagedConditionalAccess.CreateValue(node, receiver, whenNotNull, variable, semanticModel)
            .WithTriviaFrom(node);
    }

    // a?[i].M(); with an extension method M becomes if (a is { } t) { global::N.C.M(t[i]); }
    private StatementSyntax? TryRewriteConditionalAccessStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is not ConditionalAccessExpressionSyntax access ||
            !ManagedConditionalAccess.NeedsReceiverVariable(access, semanticModel))
        {
            return null;
        }

        var (receiver, whenNotNull, variable) = VisitWithReceiverVariable(access);
        return ManagedConditionalAccess.CreateStatement(receiver, whenNotNull, variable).WithTriviaFrom(node);
    }

    // The receiver and WhenNotNull rewritten in the order C# evaluates them, and the name of the pattern variable
    private (ExpressionSyntax Receiver, ExpressionSyntax WhenNotNull, string Variable) VisitWithReceiverVariable(
        ConditionalAccessExpressionSyntax node)
    {
        var receiver = Visit(node.Expression) as ExpressionSyntax ??
                       throw new InvalidOperationException($"The receiver of '{node}' did not rewrite.");
        var whenNotNull = Visit(node.WhenNotNull) as ExpressionSyntax ??
                          throw new InvalidOperationException($"The access '{node}' did not rewrite.");
        return (receiver, whenNotNull,
            GSharpInternalNames.Lowering.ConditionalReceiverPrefix + _nextConditionalReceiverId++);
    }
}
