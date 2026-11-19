namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The managed form of the source of a lowered <c>foreach</c> over an <c>ArraySegment&lt;T&gt;</c> (#1828): the
/// lowered loop reads the source by <c>Count</c> and the indexer, which do not throw for a default segment, while C#
/// enters the <c>foreach</c> with <see cref="InvalidOperationException"/>. The managed path writes the source as
/// <c>&lt;source&gt;.Slice(0)</c>, which throws that exception with the same message for a default segment and returns
/// an equal segment otherwise; the source is still evaluated once.
/// </summary>
internal static class ManagedSegmentSource
{
    /// <summary>
    /// The rewritten form of <paramref name="written"/>: <paramref name="rewritten"/> with <c>.Slice(0)</c> when
    /// lowering marked <paramref name="written"/> as the source of a <c>foreach</c> over a segment
    /// (<see cref="LoweringAnnotations.SegmentCheckAnnotationKind"/>), otherwise <paramref name="rewritten"/> as it is.
    /// </summary>
    public static SyntaxNode? Rewrite(SyntaxNode? written, SyntaxNode? rewritten) =>
        written is ExpressionSyntax { ContainsAnnotations: true } expression &&
        expression.HasAnnotations(LoweringAnnotations.SegmentCheckAnnotationKind) &&
        rewritten is ExpressionSyntax source
            ? Check(source)
            : rewritten;

    // <source>.Slice(0), with the source in parentheses unless it is a primary expression that binds tighter than
    // the member access; decided from the node Slice is appended to, which may be of another kind than the written one
    private static InvocationExpressionSyntax Check(ExpressionSyntax source) =>
        SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                source is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
                    or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax
                    ? source
                    : SyntaxFactory.ParenthesizedExpression(source),
                SyntaxFactory.IdentifierName("Slice")
            ),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))
                    )
                )
            )
        ).WithTriviaFrom(source);
}
