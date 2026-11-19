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

        // A call that returns void has no value to write: the statement form in an action invoked in place, for a
        // position the plan rejects (GSH0003), so that the managed copy is valid C# anyway
        if (semanticModel.GetTypeSymbol(node) is { SpecialType: SpecialType.System_Void })
        {
            return ManagedConditionalAccess.InvokeStatement(RewriteConditionalAccessStatement(node))
                .WithTriviaFrom(node);
        }

        var (receiver, whenNotNull, variable) = VisitWithReceiverVariable(node);
        return ManagedConditionalAccess.CreateValue(node, receiver, whenNotNull, variable, semanticModel)
            .WithTriviaFrom(node);
    }

    // a?[i].M(); with an extension method M becomes if (a is { } t) { global::N.C.M(t[i]); }
    private StatementSyntax? TryRewriteConditionalAccessStatement(ExpressionStatementSyntax node) =>
        node.Expression is ConditionalAccessExpressionSyntax access &&
        ManagedConditionalAccess.NeedsReceiverVariable(access, semanticModel)
            ? RewriteConditionalAccessStatement(access).WithTriviaFrom(node)
            : null;

    // A WhenNotNull that is an access needing its own variable is a statement as well (h?.Data?.M() becomes
    // if (h is { } t0) { if (t0.Data is { } t1) { M(t1); } }): a call that returns void has no value to write
    private StatementSyntax RewriteConditionalAccessStatement(ConditionalAccessExpressionSyntax node)
    {
        var receiver = VisitReceiver(node);
        var body = node.WhenNotNull is ConditionalAccessExpressionSyntax nested &&
                   ManagedConditionalAccess.NeedsReceiverVariable(nested, semanticModel)
            ? RewriteConditionalAccessStatement(nested)
            : SyntaxFactory.ExpressionStatement(VisitWhenNotNull(node));
        return ManagedConditionalAccess.CreateStatement(receiver, body, NextConditionalReceiver());
    }

    // The receiver and WhenNotNull rewritten in the order C# evaluates them, and the name of the pattern variable
    private (ExpressionSyntax Receiver, ExpressionSyntax WhenNotNull, string Variable) VisitWithReceiverVariable(
        ConditionalAccessExpressionSyntax node) =>
        (VisitReceiver(node), VisitWhenNotNull(node), NextConditionalReceiver());

    private ExpressionSyntax VisitReceiver(ConditionalAccessExpressionSyntax node) =>
        Visit(node.Expression) as ExpressionSyntax ??
        throw new InvalidOperationException($"The receiver of '{node}' did not rewrite.");

    private ExpressionSyntax VisitWhenNotNull(ConditionalAccessExpressionSyntax node) =>
        Visit(node.WhenNotNull) as ExpressionSyntax ??
        throw new InvalidOperationException($"The access '{node}' did not rewrite.");

    private string NextConditionalReceiver() =>
        GSharpInternalNames.Lowering.ConditionalReceiverPrefix + _nextConditionalReceiverId++;
}
