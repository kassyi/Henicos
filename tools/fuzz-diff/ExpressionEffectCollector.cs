using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Walks an expression and collects the locals and parameters it reads and writes, so that two statements can be
/// swapped when neither writes what the other reads or writes. An expression the sets cannot describe (a call,
/// division, a property, a static field, any array access except a safe <c>input[gid]</c> read) is rejected and
/// becomes a wall that nothing moves across.
/// </summary>
internal sealed class ExpressionEffectCollector(EffectScope scope) : CSharpSyntaxWalker
{
    /// <summary>Gets the locals and parameters the visited expressions read.</summary>
    internal HashSet<ISymbol> Reads { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>Gets the locals and parameters the visited expressions write.</summary>
    internal HashSet<ISymbol> Writes { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>Gets whether a visited node has effects the read and write sets cannot describe.</summary>
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
    public override void VisitInvocationExpression(InvocationExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitRefExpression(RefExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitThrowExpression(ThrowExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        switch (node.Kind())
        {
            case SyntaxKind.PointerIndirectionExpression or SyntaxKind.AddressOfExpression:
                Reject();
                return;

            case SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression:
                RecordIncrementTarget(node.Operand);
                break;
        }

        base.VisitPrefixUnaryExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        if (node.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression)
        {
            RecordIncrementTarget(node.Operand);
        }

        base.VisitPostfixUnaryExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.Kind() is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression ||
            scope.Model.GetSymbolInfo(node).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
        {
            Reject();
            return;
        }

        base.VisitBinaryExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (node.Kind() is SyntaxKind.DivideAssignmentExpression or SyntaxKind.ModuloAssignmentExpression ||
            !TryRecordAssignmentTarget(node.Left, isCompound: !node.IsKind(SyntaxKind.SimpleAssignmentExpression)))
        {
            Reject();
            return;
        }

        base.VisitAssignmentExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var memberSymbol = scope.Model.GetSymbolInfo(node).Symbol;
        if (memberSymbol is null or IPropertySymbol or IFieldSymbol { IsStatic: true, IsConst: false })
        {
            Reject();
            return;
        }

        base.VisitMemberAccessExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        // Any write or by-ref access to an array element is a wall: distinct array parameters may be the same array.
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
        switch (symbol)
        {
            case ITypeSymbol or INamespaceSymbol:
                return;

            case IFieldSymbol field:
                if (field.IsStatic && !field.IsConst)
                {
                    Reject();
                }

                return;

            case ILocalSymbol or IParameterSymbol:
                // The target of `x = ...` is recorded as a write by the assignment; its old value is not read.
                if (!IsSimpleAssignmentTarget(node))
                {
                    Reads.Add(symbol);
                }

                return;

            default:
                Reject();
                return;
        }
    }

    private static bool IsSimpleAssignmentTarget(IdentifierNameSyntax id) =>
        id.Parent is AssignmentExpressionSyntax assign &&
        assign.Left == id &&
        assign.IsKind(SyntaxKind.SimpleAssignmentExpression);

    // `x++`, `--s.F`: reads and writes the local or parameter that holds the value.
    private void RecordIncrementTarget(ExpressionSyntax operand)
    {
        if (!TryUnwrapRootStorageSymbol(operand, out var target))
        {
            Reject();
            return;
        }

        Reads.Add(target);
        Writes.Add(target);
    }

    private bool TryRecordAssignmentTarget(ExpressionSyntax left, bool isCompound)
    {
        if (left is IdentifierNameSyntax id)
        {
            if (!IsMutableValueSymbol(scope.Model.GetSymbolInfo(id).Symbol, out var targetSymbol))
            {
                return false;
            }

            Writes.Add(targetSymbol);
            if (isCompound)
            {
                Reads.Add(targetSymbol);
            }

            return true;
        }

        // A struct field write changes the whole local, and the other fields keep their values: read and write.
        if (left is MemberAccessExpressionSyntax member &&
            scope.Model.GetSymbolInfo(member).Symbol is IFieldSymbol { IsStatic: false } &&
            TryUnwrapRootStorageSymbol(member, out var rootSymbol))
        {
            Reads.Add(rootSymbol);
            Writes.Add(rootSymbol);
            return true;
        }

        return false;
    }

    private bool TryUnwrapRootStorageSymbol(ExpressionSyntax expr, [NotNullWhen(true)] out ISymbol? storageSymbol)
    {
        ExpressionSyntax current = SkipParentheses(expr);
        while (current is MemberAccessExpressionSyntax member)
        {
            if (scope.Model.GetSymbolInfo(member).Symbol is not IFieldSymbol { IsStatic: false })
            {
                storageSymbol = null;
                return false;
            }

            current = SkipParentheses(member.Expression);
        }

        if (current is IdentifierNameSyntax id &&
            IsMutableValueSymbol(scope.Model.GetSymbolInfo(id).Symbol, out storageSymbol))
        {
            return true;
        }

        storageSymbol = null;
        return false;
    }

    private static ExpressionSyntax SkipParentheses(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax paren)
        {
            expr = paren.Expression;
        }

        return expr;
    }

    private static bool IsMutableValueSymbol(ISymbol? candidate, [NotNullWhen(true)] out ISymbol? valueSymbol)
    {
        switch (candidate)
        {
            case ILocalSymbol { IsRef: false, IsConst: false } local
                when local.Type.IsValueType && !local.Type.IsRefLikeType && local.Type.TypeKind != TypeKind.Error:
                valueSymbol = local;
                return true;

            case IParameterSymbol { RefKind: RefKind.None } param
                when param.Type.IsValueType && !param.Type.IsRefLikeType && param.Type.TypeKind != TypeKind.Error:
                valueSymbol = param;
                return true;

            default:
                valueSymbol = null;
                return false;
        }
    }

    private void Reject() => IsRejected = true;
}
