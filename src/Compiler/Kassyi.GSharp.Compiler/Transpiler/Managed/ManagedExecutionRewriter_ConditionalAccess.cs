namespace Kassyi.GSharp.Compiler.Transpiler;

internal sealed partial class ManagedExecutionRewriter
{
    private ManagedConditionalAccessWriter? _conditionalAccessWriter;

    // The null-conditional accesses that call an extension method on a binding (ManagedConditionalAccessWriter)
    private ManagedConditionalAccessWriter ConditionalAccessWriter =>
        _conditionalAccessWriter ??= new ManagedConditionalAccessWriter(semanticModel, Visit);

    // a?[i].M() with an extension method M becomes (a is { } t ? (R)(global::N.C.M(t[i])) : default(R)); any other
    // access stays a null-conditional access
    public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node) =>
        ConditionalAccessWriter.TryVisitAccess(node) ?? base.VisitConditionalAccessExpression(node);

    /// <summary>
    /// The block body of a void method whose expression body is a null-conditional access that calls an extension
    /// method on a binding, or <see langword="null"/>; the managed copy writes it in place of the expression body.
    /// </summary>
    public BlockSyntax? TryRewriteBody(ArrowExpressionClauseSyntax body) =>
        ConditionalAccessWriter.TryWriteBody(body);
}
