namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>
/// What the receiver of a null-conditional access is, which decides how its non-null test is written.
/// </summary>
internal enum ConditionalReceiverKind
{
    /// <summary>An array or span-like value: null when its <c>data</c> pointer is <c>nullptr</c>.</summary>
    ArrayLike,

    /// <summary>A C++ stack array: never null, so the access is written as it is.</summary>
    NeverNull,

    /// <summary>A <c>Nullable&lt;S&gt;</c>: tested by <c>HasValue</c>, accessed through <c>Value</c>.</summary>
    Nullable,

    /// <summary>A receiver the transpiler cannot test for null (reported at the <c>?.</c>).</summary>
    Unsupported
}

/// <summary>How the value of a null-conditional access is written.</summary>
internal enum ConditionalResultForm
{
    /// <summary>The access has a value type <c>T</c>: the result is a <c>Nullable&lt;T&gt;</c>.</summary>
    Lifted,

    /// <summary>
    /// The access is already nullable or an array: the result is the access or the null representation.
    /// </summary>
    Passthrough,

    /// <summary>The result is not used (a call that returns <see langword="void" />, a statement).</summary>
    Statement
}

/// <summary>
/// The decision how a <c>a?.b</c> is written: its receiver kind, result form and result type, and the GSH0003 text
/// (<paramref name="Problem"/>), which is set exactly when the receiver kind is
/// <see cref="ConditionalReceiverKind.Unsupported"/>.
/// </summary>
internal readonly record struct ConditionalAccessPlan(
    ConditionalReceiverKind Receiver,
    ConditionalResultForm Form,
    ITypeSymbol? ResultType,
    string? Problem = null);

/// <summary>
/// The null-conditional access <c>a?.b</c>, <c>a?[i]</c> and <c>a?.M()</c>: the receiver is evaluated once into a
/// temporary, the access is written for the receiver's value when it is not null, and the result is the access lifted
/// to a nullable (or an array / nullable result as it is), or null.
/// </summary>
internal static class ConditionalAccess
{
    /// <summary>The GSH0003 text of a null-conditional access whose receiver cannot be tested for null.</summary>
    public const string UnsupportedReceiver = "?. on a receiver that is neither an array nor a Nullable<T>";

    /// <summary>The GSH0003 text of <c>p?[i]</c> on a <c>Nullable&lt;T&gt;</c>.</summary>
    public const string NullableElementAccess = "?[] on a Nullable<T> receiver";

    /// <summary>The GSH0003 text of <c>?.</c> or <c>?[]</c> on a <c>Nullable&lt;T&gt;</c> of an array view.</summary>
    public const string NullableArrayView = "?. on a Nullable<T> of an array view";

    /// <summary>The GSH0003 text of <c>?.</c> on a <c>Nullable&lt;T&gt;</c> of a vector (<c>Vector2?</c>).</summary>
    public const string NullableVector = "?. on a Nullable<T> of a vector";

    /// <summary>
    /// The GSH0003 text of a <c>?.</c> that calls an extension method returning <see langword="void"/> on a binding
    /// outside a statement (<c>for (...; ...; i++, q?[t].M())</c>).
    /// </summary>
    public const string VoidExtensionCallOutsideStatement =
        "?. calling an extension method that returns void outside a statement, which the managed CPU path cannot write";

    /// <summary>Decides how <paramref name="node"/> is written.</summary>
    /// <exception cref="InvalidOperationException">
    /// The type of the receiver or of the access did not resolve.
    /// </exception>
    public static ConditionalAccessPlan Plan(ConditionalAccessExpressionSyntax node, SemanticModel model)
    {
        var receiverType = model.GetTypeSymbol(node.Expression) ?? throw Unresolved("receiver", node.Expression);
        var (kind, problem) = GetReceiverKind(node, receiverType, model);

        var accessType = model.GetTypeSymbol(node.WhenNotNull) ?? throw Unresolved("access", node.WhenNotNull);
        if (accessType.SpecialType == SpecialType.System_Void || node.Parent is ExpressionStatementSyntax)
        {
            return problem == null && IsVoidCallOutsideStatement(node, accessType, model)
                ? new ConditionalAccessPlan(ConditionalReceiverKind.Unsupported, ConditionalResultForm.Statement, null,
                    VoidExtensionCallOutsideStatement)
                : new ConditionalAccessPlan(kind, ConditionalResultForm.Statement, null, problem);
        }

        var resultType = model.GetTypeSymbol(node) ?? throw Unresolved("access", node);
        var form = NullRepresentation.IsNullComparable(accessType)
            ? ConditionalResultForm.Passthrough
            : ConditionalResultForm.Lifted;
        return new ConditionalAccessPlan(kind, form, resultType, problem);
    }

    // An extension method that returns void, called on a binding outside a statement (a for incrementor): the managed
    // copy writes such a call as a statement on the receiver in a pattern variable (ManagedConditionalAccess), and C#
    // has no expression for it
    private static bool IsVoidCallOutsideStatement(ConditionalAccessExpressionSyntax node, ITypeSymbol accessType,
        SemanticModel model) =>
        accessType.SpecialType == SpecialType.System_Void &&
        !ManagedConditionalAccess.IsStatement(node, model) &&
        ManagedConditionalAccess.NeedsReceiverVariable(node, model);

    private static InvalidOperationException Unresolved(string part, SyntaxNode node) =>
        new($"The type of the {part} '{node}' did not resolve.");

    // The receiver kind, and the GSH0003 text when the receiver cannot be tested for null or its value cannot be read
    private static (ConditionalReceiverKind Kind, string? Problem) GetReceiverKind(
        ConditionalAccessExpressionSyntax node, ITypeSymbol receiverType, SemanticModel model)
    {
        if (receiverType.IsArrayLikeType())
        {
            return StackArrayLocals.IsStackArrayReference(node.Expression, model)
                ? (ConditionalReceiverKind.NeverNull, null)
                : (ConditionalReceiverKind.ArrayLike, null);
        }

        if (receiverType is not INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var valueType]
            })
        {
            return (ConditionalReceiverKind.Unsupported, UnsupportedReceiver);
        }

        return GetNullableProblem(node, valueType) is { } problem
            ? (ConditionalReceiverKind.Unsupported, problem)
            : (ConditionalReceiverKind.Nullable, null);
    }

    // The value of a Nullable<T> receiver is written without its type, so a member that the receiver's type maps (an
    // array view's Count or Length, a vector's X) or an indexer of the value cannot be written: the GSH0003 text, or
    // null when the access can be written
    private static string? GetNullableProblem(ConditionalAccessExpressionSyntax node, ITypeSymbol valueType)
    {
        if (valueType.IsArrayLikeType())
        {
            return NullableArrayView;
        }

        if (valueType.IsSystemNumericsVector())
        {
            return NullableVector;
        }

        return FirstBinding(node) is ElementBindingExpressionSyntax ? NullableElementAccess : null;
    }

    // The binding that reads the receiver of node: the first one in document order, since WhenNotNull starts with it
    // and the bindings of a nested access come after it
    private static SyntaxNode? FirstBinding(ConditionalAccessExpressionSyntax node) =>
        node.WhenNotNull
            .DescendantNodesAndSelf()
            .AsStackEnumerable()
            .FirstOrDefault(static n => n is MemberBindingExpressionSyntax or ElementBindingExpressionSyntax);

    /// <summary>
    /// The receiver of a <see cref="ConditionalReceiverKind.NeverNull"/> access (a stack array), read as written.
    /// </summary>
    public static ExpressionSyntax NeverNullReceiver(ConditionalAccessExpressionSyntax node, Compilation compilation) =>
        AnnotateSource(node.Expression, compilation);

    /// <summary>
    /// <paramref name="access"/> (the <see cref="ConditionalAccessExpressionSyntax.WhenNotNull" /> of a
    /// null-conditional access) with its member and element bindings read from <paramref name="receiver" />. A nested
    /// null-conditional access keeps its own bindings; only its receiver is rewritten.
    /// </summary>
    /// <remarks>
    /// Every node carries the original-span annotation of the node it replaces, so the semantic queries of the rules
    /// for <c>x.b</c>, <c>x[i]</c> and <c>x.M()</c> resolve to the binding they stand for.
    /// </remarks>
    public static ExpressionSyntax BindReceiver(ExpressionSyntax access, ExpressionSyntax receiver,
        Compilation compilation) =>
        ReplaceBindings(AnnotateSource(access, compilation), receiver);

    /// <summary>
    /// The annotation of a placeholder that stands for the receiver itself (the first argument of the static call of
    /// <c>s?.M()</c> on the managed path); <see cref="ReplaceBindings{TNode}"/> replaces it with the receiver.
    /// </summary>
    public static readonly SyntaxAnnotation ReceiverPlaceholder = new("GSharpConditionalReceiver");

    /// <summary>
    /// <paramref name="node"/> (the <c>WhenNotNull</c> of a null-conditional access, or what was written for it) with
    /// its member and element bindings, and each <see cref="ReceiverPlaceholder"/>, read from
    /// <paramref name="receiver"/>. A nested null-conditional access keeps the bindings of its own <c>WhenNotNull</c>;
    /// only its receiver is rewritten. Each replacement keeps the original-span annotation of the binding.
    /// </summary>
    public static TNode ReplaceBindings<TNode>(TNode node, ExpressionSyntax receiver) where TNode : SyntaxNode =>
        new BindingReplacer(receiver).Visit(node) as TNode ??
        throw new InvalidOperationException($"The bindings of '{node}' did not rewrite to the same kind of node.");

    // A node of the compilation gets the original-span annotation of its own position. A node of a lowered tree
    // already carries the span of the node it stands for, and the nodes lowering built have none on purpose: a span
    // of their position in the lowered tree would resolve to an unrelated node of the source
    private static ExpressionSyntax AnnotateSource(ExpressionSyntax node, Compilation compilation) =>
        compilation.ContainsSyntaxTree(node.SyntaxTree)
            ? LoweringAnnotations.AnnotateOriginalSpans(node, compilation)
            : node;

    private sealed class BindingReplacer(ExpressionSyntax receiver) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberBindingExpression(MemberBindingExpressionSyntax node) =>
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, node.Name)
                .WithOriginalSpanFrom(node);

        public override SyntaxNode? VisitElementBindingExpression(ElementBindingExpressionSyntax node) =>
            SyntaxFactory.ElementAccessExpression(receiver, node.ArgumentList)
                .WithOriginalSpanFrom(node);

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            node.HasAnnotation(ReceiverPlaceholder) ? receiver.WithTriviaFrom(node) : node;

        // The bindings of WhenNotNull belong to the inner access; only its receiver reads the outer one
        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node) =>
            node.WithExpression(Visit(node.Expression) as ExpressionSyntax ??
                                throw new InvalidOperationException(
                                    $"The receiver of '{node}' did not rewrite to an expression."));
    }
}
