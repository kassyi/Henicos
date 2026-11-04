using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Walks an expression and rejects anything that writes, calls, allocates on the stack, takes an address, or reads
/// state another thread or a call could change (properties, static fields, array elements other than a safe
/// <c>input[gid]</c>).
/// </summary>
internal class SideEffectFreeChecker(EffectScope scope) : CSharpSyntaxWalker
{
    /// <summary>Gets whether a visited node made the expression unsafe.</summary>
    internal bool IsRejected { get; private set; }

    /// <inheritdoc/>
    public override void Visit(SyntaxNode? node)
    {
        if (!IsRejected)
        {
            base.Visit(node);
        }
    }

    /// <inheritdoc/>
    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitInvocationExpression(InvocationExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitRefExpression(RefExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitDeclarationExpression(DeclarationExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitThrowExpression(ThrowExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitAwaitExpression(AwaitExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        if (node.Kind() is SyntaxKind.PreIncrementExpression
            or SyntaxKind.PreDecrementExpression
            or SyntaxKind.PointerIndirectionExpression
            or SyntaxKind.AddressOfExpression)
        {
            Reject();
            return;
        }

        base.VisitPrefixUnaryExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        if (node.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression)
        {
            Reject();
            return;
        }

        base.VisitPostfixUnaryExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (scope.Model.GetSymbolInfo(node).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
        {
            Reject();
            return;
        }

        base.VisitBinaryExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var memberSymbol = scope.Model.GetSymbolInfo(node).Symbol;
        if (memberSymbol is null or IPropertySymbol || IsMutableStaticField(memberSymbol))
        {
            Reject();
            return;
        }

        base.VisitMemberAccessExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        if (!MetamorphicPurity.IsSafeInputElementRead(node, scope))
        {
            Reject();
            return;
        }

        base.VisitElementAccessExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (FuzzSyntaxHelpers.IsMemberAccessRight(node))
        {
            return;
        }

        var symbol = scope.Model.GetSymbolInfo(node).Symbol;
        if (symbol is null || IsMutableStaticField(symbol))
        {
            Reject();
        }
    }

    /// <summary>Marks the expression as unsafe; the walk stops at the next node.</summary>
    protected void Reject() => IsRejected = true;

    private static bool IsMutableStaticField(ISymbol symbol) =>
        symbol is IFieldSymbol { IsStatic: true, IsConst: false };
}
