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
    private static readonly SymbolDisplayFormat s_typeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.ExpandNullable);

    /// <summary>
    /// A placeholder for the receiver itself as the first argument of a static call (<c>s?.M()</c>), replaced by the
    /// pattern variable (<see cref="ConditionalAccess.ReceiverPlaceholder"/>).
    /// </summary>
    public static ExpressionSyntax CreateReceiverMarker() =>
        SyntaxFactory.IdentifierName(GSharpInternalNames.Lowering.ConditionalReceiverPrefix).WithAdditionalAnnotations(
            ConditionalAccess.ReceiverPlaceholder);

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
    /// that of an access nested in it) calls an extension method on a binding of <paramref name="node"/>, or holds an
    /// access that needs one, whose receiver starts with a binding of <paramref name="node"/>.
    /// </summary>
    public static bool NeedsReceiverVariable(ConditionalAccessExpressionSyntax node, SemanticModel semanticModel) =>
        node.WhenNotNull
            .DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any((Access: node, Model: semanticModel), static (n, state) =>
                n switch
                {
                    InvocationExpressionSyntax call =>
                        IsCallOnBinding(call) &&
                        FindOwner(call) == state.Access &&
                        state.Model.GetSymbol(call) is IMethodSymbol { ReducedFrom: not null },
                    ConditionalAccessExpressionSyntax nested =>
                        FindOwner(nested) == state.Access && NeedsReceiverVariable(nested, state.Model),
                    _ => false
                });

    /// <summary>
    /// Whether <paramref name="node"/> is a statement: it is used as one, it is the <c>WhenNotNull</c> of an access
    /// that is (the inner access of <c>h?.Data?.M();</c>), or it is the body of a <c>Gpu.ForUnroll</c> or
    /// <c>Gpu.SyncGrid</c> lambda, which is inlined as a statement. Decided on the node of the user's tree that
    /// <paramref name="node"/> stands for: the inner access of a chain is planned on a tree of its own.
    /// </summary>
    public static bool IsStatement(ConditionalAccessExpressionSyntax node, SemanticModel semanticModel)
    {
        var original = semanticModel.GetOriginalNode(node) as ConditionalAccessExpressionSyntax ?? node;
        return IsInStatementPosition(original, semanticModel.Compilation);
    }

    private static bool IsInStatementPosition(ConditionalAccessExpressionSyntax node, Compilation compilation) =>
        node.Parent switch
        {
            ExpressionStatementSyntax => true,
            ConditionalAccessExpressionSyntax outer when outer.WhenNotNull == node =>
                IsInStatementPosition(outer, compilation),
            AnonymousFunctionExpressionSyntax lambda when lambda.ExpressionBody == node =>
                compilation.ContainsSyntaxTree(lambda.SyntaxTree) &&
                SplitPointPlacement.IsInlinedMacroBody(lambda, compilation.GetCachedSemanticModel(lambda.SyntaxTree)),
            _ => false
        };

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
        // Nullable<T> spelled out: the managed source is printed by NormalizeWhitespace, which writes (int? )
        var typeSyntax = SyntaxFactory.ParseTypeName(type.ToDisplayString(s_typeFormat));
        return SyntaxFactory.ParenthesizedExpression(
            SyntaxFactory.ConditionalExpression(
                CreateTest(receiver, variable),
                SyntaxFactory.CastExpression(typeSyntax,
                    SyntaxFactory.ParenthesizedExpression(
                        ConditionalAccess.ReplaceBindings(whenNotNull, SyntaxFactory.IdentifierName(variable)))),
                SyntaxFactory.DefaultExpression(typeSyntax)
            )
        );
    }

    /// <summary>
    /// <c>if (r is { } t) { &lt;access on t&gt; }</c> for an access used as a statement; <paramref name="body"/> is
    /// the statement written for its <c>WhenNotNull</c>.
    /// </summary>
    public static StatementSyntax CreateStatement(ExpressionSyntax receiver, StatementSyntax body, string variable) =>
        SyntaxFactory.IfStatement(
            CreateTest(receiver, variable),
            SyntaxFactory.Block(ConditionalAccess.ReplaceBindings(body, SyntaxFactory.IdentifierName(variable)))
        );

    /// <summary><c>((global::System.Action)(() =&gt; { &lt;statement&gt; }))()</c>.</summary>
    public static ExpressionSyntax InvokeStatement(StatementSyntax statement) =>
        SyntaxFactory.InvocationExpression(
            SyntaxFactory.ParenthesizedExpression(
                SyntaxFactory.CastExpression(
                    SyntaxFactory.ParseTypeName("global::System.Action"),
                    SyntaxFactory.ParenthesizedExpression(
                        SyntaxFactory.ParenthesizedLambdaExpression(SyntaxFactory.Block(statement))))));

    // r is { } t
    private static ExpressionSyntax CreateTest(ExpressionSyntax receiver, string variable) =>
        SyntaxFactory.IsPatternExpression(
            SyntaxFactory.ParenthesizedExpression(receiver.WithoutTrivia()),
            SyntaxFactory.RecursivePattern()
                .WithPropertyPatternClause(SyntaxFactory.PropertyPatternClause())
                .WithDesignation(SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(variable)))
        );

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
}
