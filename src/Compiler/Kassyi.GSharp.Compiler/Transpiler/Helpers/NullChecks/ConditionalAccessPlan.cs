using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>What the receiver of a null-conditional access is, which decides how its non-null test is written.</summary>
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

    /// <summary>The access is already nullable or an array: the result is the access or the null representation.</summary>
    Passthrough,

    /// <summary>The result is not used (a call that returns <see langword="void" />, a statement).</summary>
    Statement
}

/// <summary>The decision how a <c>a?.b</c> is written: its receiver kind, result form and result type.</summary>
internal readonly record struct ConditionalAccessPlan(
    ConditionalReceiverKind Receiver,
    ConditionalResultForm Form,
    ITypeSymbol? ResultType);

/// <summary>
/// The null-conditional access <c>a?.b</c>, <c>a?[i]</c> and <c>a?.M()</c>: the receiver is evaluated once into a
/// temporary, the access is written for the receiver's value when it is not null, and the result is the access lifted to
/// a nullable (or an array / nullable result as it is), or null.
/// </summary>
internal static class ConditionalAccess
{
    /// <summary>Decides how <paramref name="node"/> is written.</summary>
    /// <exception cref="InvalidOperationException">The type of the receiver or of the access did not resolve.</exception>
    public static ConditionalAccessPlan Plan(ConditionalAccessExpressionSyntax node, SemanticModel model)
    {
        var receiverType = model.GetTypeSymbol(node.Expression) ??
                           throw new InvalidOperationException($"The type of the receiver '{node.Expression}' did not resolve.");
        var kind = GetReceiverKind(receiverType, node.Expression, model);

        // The operation, not the syntax: the type of the binding [1] of a lowered a?[1] is not found by its span, which
        // an argument list shares
        var accessType = (model.GetSafeOperation(node) as IConditionalAccessOperation)?.WhenNotNull.Type ??
                         model.GetTypeSymbol(node.WhenNotNull) ??
                         throw new InvalidOperationException($"The type of the access '{node.WhenNotNull}' did not resolve.");
        if (accessType.SpecialType == SpecialType.System_Void || node.Parent is ExpressionStatementSyntax)
        {
            return new ConditionalAccessPlan(kind, ConditionalResultForm.Statement, null);
        }

        var resultType = model.GetTypeSymbol(node) ??
                         throw new InvalidOperationException($"The type of the access '{node}' did not resolve.");
        var form = accessType.IsArrayLikeType() || NullRepresentation.IsNullComparable(accessType)
            ? ConditionalResultForm.Passthrough
            : ConditionalResultForm.Lifted;
        return new ConditionalAccessPlan(kind, form, resultType);
    }

    private static ConditionalReceiverKind GetReceiverKind(ITypeSymbol receiverType, ExpressionSyntax receiver,
        SemanticModel model) =>
        receiverType.IsArrayLikeType()
            ? StackArrayLocals.IsStackArrayReference(receiver, model)
                ? ConditionalReceiverKind.NeverNull
                : ConditionalReceiverKind.ArrayLike
            : receiverType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                ? ConditionalReceiverKind.Nullable
                : ConditionalReceiverKind.Unsupported;

    /// <summary>
    /// <paramref name="access"/> (the <see cref="ConditionalAccessExpressionSyntax.WhenNotNull" /> of a null-conditional
    /// access) with its member and element bindings read from <paramref name="receiver" />. A nested null-conditional
    /// access keeps its own bindings; only its receiver is rewritten.
    /// </summary>
    /// <remarks>
    /// Every node carries the original-span annotation of the node it replaces, so the semantic queries of the rules for
    /// <c>x.b</c>, <c>x[i]</c> and <c>x.M()</c> resolve to the binding they stand for.
    /// </remarks>
    public static ExpressionSyntax BindReceiver(ExpressionSyntax access, ExpressionSyntax receiver,
        Compilation compilation)
    {
        var annotated = LoweringAnnotations.AnnotateOriginalSpans(access, compilation);
        return (ExpressionSyntax)new BindingReplacer(receiver).Visit(annotated)!;
    }

    private sealed class BindingReplacer(ExpressionSyntax receiver) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberBindingExpression(MemberBindingExpressionSyntax node) =>
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, node.Name)
                .WithOriginalSpanFrom(node);

        public override SyntaxNode? VisitElementBindingExpression(ElementBindingExpressionSyntax node) =>
            SyntaxFactory.ElementAccessExpression(receiver, node.ArgumentList)
                .WithOriginalSpanFrom(node);

        // The bindings of WhenNotNull belong to the inner access; only its receiver reads the outer one
        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node) =>
            node.WithExpression((ExpressionSyntax)Visit(node.Expression)!);
    }
}
