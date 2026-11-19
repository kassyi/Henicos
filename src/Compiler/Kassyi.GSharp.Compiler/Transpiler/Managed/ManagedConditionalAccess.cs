namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The managed copy of a null-conditional access that calls an extension method on one of its bindings
/// (<c>q?[i].M()</c>, <c>h?.Inner.M()</c>, <c>s?.M()</c>): the receiver goes into a pattern variable, and the access
/// is written on it, with the call as the static call of the method that the semantic model chose.
/// </summary>
/// <remarks>
/// [WHY] <see cref="ManagedNameQualifier"/> writes an extension call as the static call with its receiver as the first
/// argument, because the generated file has none of the user's <c>using</c> directives. Inside <c>WhenNotNull</c> the
/// receiver is a binding (<c>[i]</c>, <c>.Inner</c>), which cannot stand alone; importing the class into the generated
/// file instead would let C# resolve the call again in another scope and pick another method (or none). The access
/// becomes <c>(r is { } t ? (R)(M(t[i])) : default(R))</c>, or <c>if (r is { } t) { M(ref t[i]); }</c> as a statement:
/// the receiver is evaluated once, the rest only when it is not null, as C# does.
/// </remarks>
internal static class ManagedConditionalAccess
{
    /// <summary>
    /// The annotation of the placeholder that stands for the receiver itself as the first argument of a static call
    /// (<c>s?.M()</c>), replaced by the pattern variable.
    /// </summary>
    public static readonly SyntaxAnnotation ReceiverMarker = new("GSharpConditionalReceiver");

    /// <summary>The placeholder for the receiver (<see cref="ReceiverMarker"/>).</summary>
    public static ExpressionSyntax CreateReceiverMarker() =>
        SyntaxFactory.IdentifierName(GSharpInternalNames.Lowering.ConditionalReceiverPrefix).WithAdditionalAnnotations(
            ReceiverMarker);

    /// <summary>
    /// The type of the value of the receiver of the null-conditional access that holds <paramref name="node"/>: the
    /// receiver's type, or <c>T</c> of a <c>Nullable&lt;T&gt;</c>, which is the type of the pattern variable.
    /// </summary>
    public static ITypeSymbol? GetValueType(SyntaxNode node, SemanticModel semanticModel) =>
        FindOwner(node) is { } access && semanticModel.GetTypeSymbol(access.Expression) is { } type
            ? type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var value]
            }
                ? value
                : type
            : null;

    /// <summary>
    /// Whether the managed copy of <paramref name="node"/> needs the pattern variable: its <c>WhenNotNull</c> (not
    /// that of an access nested in it) calls an extension method on a binding of <paramref name="node"/>.
    /// </summary>
    public static bool NeedsReceiverVariable(ConditionalAccessExpressionSyntax node, SemanticModel semanticModel) =>
        node.WhenNotNull
            .DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any((Access: node, Model: semanticModel), static (n, state) =>
                n is InvocationExpressionSyntax call &&
                IsCallOnBinding(call) &&
                FindOwner(call) == state.Access &&
                state.Model.GetSymbol(call) is IMethodSymbol { ReducedFrom: not null });

    /// <summary>
    /// <c>(r is { } t ? (R)(&lt;access on t&gt;) : default(R))</c> for <paramref name="node"/>, whose receiver and
    /// <c>WhenNotNull</c> the managed rewriter has rewritten into <paramref name="receiver"/> and
    /// <paramref name="whenNotNull"/>.
    /// </summary>
    public static ExpressionSyntax CreateValue(ConditionalAccessExpressionSyntax node, ExpressionSyntax receiver,
        ExpressionSyntax whenNotNull, string variable, SemanticModel semanticModel)
    {
        var type = semanticModel.GetTypeSymbol(node) ??
                   throw new InvalidOperationException($"The type of the access '{node}' did not resolve.");
        var typeSyntax = SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return SyntaxFactory.ParenthesizedExpression(
            SyntaxFactory.ConditionalExpression(
                CreateTest(receiver, variable),
                SyntaxFactory.CastExpression(typeSyntax,
                    SyntaxFactory.ParenthesizedExpression(BindReceiver(whenNotNull, variable))),
                SyntaxFactory.DefaultExpression(typeSyntax)
            )
        );
    }

    /// <summary><c>if (r is { } t) { &lt;access on t&gt;; }</c> for an access used as a statement.</summary>
    public static StatementSyntax CreateStatement(ExpressionSyntax receiver, ExpressionSyntax whenNotNull,
        string variable) =>
        SyntaxFactory.IfStatement(
            CreateTest(receiver, variable),
            SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(BindReceiver(whenNotNull, variable)))
        );

    // r is { } t
    private static ExpressionSyntax CreateTest(ExpressionSyntax receiver, string variable) =>
        SyntaxFactory.IsPatternExpression(
            SyntaxFactory.ParenthesizedExpression(receiver.WithoutTrivia()),
            SyntaxFactory.RecursivePattern()
                .WithPropertyPatternClause(SyntaxFactory.PropertyPatternClause())
                .WithDesignation(SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(variable)))
        );

    private static ExpressionSyntax BindReceiver(ExpressionSyntax whenNotNull, string variable) =>
        new BindingReplacer(SyntaxFactory.IdentifierName(variable)).Visit(whenNotNull) as ExpressionSyntax ??
        throw new InvalidOperationException($"The bindings of '{whenNotNull}' did not rewrite to an expression.");

    // A reduced call on the receiver (?.M()) or whose receiver chain starts with a binding (?[i].M(), ?.A.B.M())
    private static bool IsCallOnBinding(InvocationExpressionSyntax call) =>
        call.Expression switch
        {
            MemberBindingExpressionSyntax => true,
            MemberAccessExpressionSyntax access => StartsWithBinding(access.Expression),
            _ => false
        };

    private static bool StartsWithBinding(ExpressionSyntax receiver) =>
        receiver switch
        {
            MemberBindingExpressionSyntax or ElementBindingExpressionSyntax => true,
            MemberAccessExpressionSyntax member => StartsWithBinding(member.Expression),
            ElementAccessExpressionSyntax element => StartsWithBinding(element.Expression),
            InvocationExpressionSyntax invocation => StartsWithBinding(invocation.Expression),
            PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed =>
                StartsWithBinding(suppressed.Operand),
            _ => false
        };

    // The access whose bindings node reads: the nearest one that holds node in its WhenNotNull
    private static ConditionalAccessExpressionSyntax? FindOwner(SyntaxNode node)
    {
        for (var current = node; current.Parent is { } parent; current = parent)
        {
            if (parent is ConditionalAccessExpressionSyntax access && access.WhenNotNull == current)
            {
                return access;
            }
        }

        return null;
    }

    // The bindings of the access, and the receiver placeholder, read from the pattern variable; an access nested in
    // WhenNotNull keeps the bindings of its own WhenNotNull
    private sealed class BindingReplacer(IdentifierNameSyntax variable) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberBindingExpression(MemberBindingExpressionSyntax node) =>
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, variable, node.Name);

        public override SyntaxNode? VisitElementBindingExpression(ElementBindingExpressionSyntax node) =>
            SyntaxFactory.ElementAccessExpression(variable, node.ArgumentList);

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            node.HasAnnotation(ReceiverMarker) ? variable.WithTriviaFrom(node) : node;

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node) =>
            node.WithExpression(Visit(node.Expression) as ExpressionSyntax ??
                                throw new InvalidOperationException(
                                    $"The receiver of '{node}' did not rewrite to an expression."));
    }
}
