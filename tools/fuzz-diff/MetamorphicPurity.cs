using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Decides which expressions a metamorphic rewrite may evaluate earlier, later, more often, or not at all
/// without changing what the kernel computes.
/// </summary>
internal static class MetamorphicPurity
{
    /// <summary>
    /// Returns <see langword="true"/> when evaluating <paramref name="expr"/> writes nothing, calls nothing, and reads
    /// nothing that another statement could change (see <see cref="SideEffectFreeChecker"/>).
    /// </summary>
    internal static bool IsSideEffectFree(ExpressionSyntax expr, EffectScope scope)
    {
        var checker = new SideEffectFreeChecker(scope);
        checker.Visit(expr);
        return !checker.IsRejected;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="expr"/> has a type, is free of side effects, and cannot
    /// throw (see <see cref="PureExpressionChecker"/>), so it may be copied, moved or duplicated.
    /// </summary>
    internal static bool IsPure(ExpressionSyntax expr, EffectScope scope)
    {
        if (!MetamorphicTypeResolver.TryResolveExpressionType(expr, scope.Model, out _))
        {
            return false;
        }

        var checker = new PureExpressionChecker(scope);
        checker.Visit(expr);
        return !checker.IsRejected;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="elem"/> reads <c>input[gid]</c> from an array of
    /// <see cref="EffectScope.SafeInputArrays"/>.
    /// </summary>
    internal static bool IsSafeInputElementRead(ElementAccessExpressionSyntax elem, EffectScope scope)
    {
        if (scope.Gid is null ||
            IsWrittenOrByRefElementAccess(elem) ||
            elem.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        var arraySymbol = scope.Model.GetSymbolInfo(elem.Expression).Symbol;
        if (arraySymbol is null || !scope.SafeInputArrays.Contains(arraySymbol))
        {
            return false;
        }

        var indexSymbol = scope.Model.GetSymbolInfo(elem.ArgumentList.Arguments[0].Expression).Symbol;
        return indexSymbol is not null && SymbolEqualityComparer.Default.Equals(indexSymbol, scope.Gid);
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="elem"/> is assigned, incremented or passed by reference.</summary>
    internal static bool IsWrittenOrByRefElementAccess(ElementAccessExpressionSyntax elem) =>
        elem.Parent switch
        {
            AssignmentExpressionSyntax assign => assign.Left == elem,
            PrefixUnaryExpressionSyntax prefix => prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression or SyntaxKind.AddressOfExpression,
            PostfixUnaryExpressionSyntax postfix => postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression,
            ArgumentSyntax arg => !arg.RefKindKeyword.IsKind(SyntaxKind.None),
            RefExpressionSyntax => true,
            _ => false,
        };

    /// <summary>
    /// Returns the side-effect-free right-hand side of <paramref name="stmt"/> when it is a single-variable
    /// declaration or an assignment to a local; the prologue, the hash fold and the checksum write are never returned.
    /// </summary>
    internal static bool TryGetTopLevelPureRhsExpression(
        StatementSyntax stmt,
        EffectScope scope,
        [NotNullWhen(true)] out ExpressionSyntax? rhsRoot)
    {
        rhsRoot = null;
        if (FuzzSyntaxHelpers.IsGidDeclaration(stmt) ||
            FuzzSyntaxHelpers.IsGidBoundsGuard(stmt) ||
            FuzzSyntaxHelpers.IsHashInitDeclaration(stmt) ||
            FuzzSyntaxHelpers.IsChecksumFoldStatement(stmt) ||
            FuzzSyntaxHelpers.IsFinalChecksumWrite(stmt))
        {
            return false;
        }

        if (stmt is LocalDeclarationStatementSyntax localDecl &&
            localDecl.Declaration.Variables.Count == 1 &&
            localDecl.Declaration.Variables[0].Initializer is { Value: { } initVal } &&
            IsSideEffectFree(initVal, scope))
        {
            rhsRoot = initVal;
            return true;
        }

        if (stmt is ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax
                {
                    Left: IdentifierNameSyntax leftId,
                    Right: { } assignRhs,
                },
            } &&
            scope.Model.GetSymbolInfo(leftId).Symbol is ILocalSymbol { IsRef: false } &&
            IsSideEffectFree(assignRhs, scope))
        {
            rhsRoot = assignRhs;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="subExpr"/> is evaluated whenever the expression
    /// <paramref name="statementBoundary"/> is: it is not inside a branch of <c>?:</c> or the right operand of
    /// <c>&amp;&amp;</c>, <c>||</c>, <c>??</c> or <c>??=</c>.
    /// </summary>
    internal static bool IsOnUnconditionalEvaluationPath(ExpressionSyntax subExpr, SyntaxNode statementBoundary)
    {
        SyntaxNode? current = subExpr;
        while (current is not null && current != statementBoundary)
        {
            var parent = current.Parent;
            if (parent is ConditionalExpressionSyntax cond &&
                (current == cond.WhenTrue || current == cond.WhenFalse))
            {
                return false;
            }

            if (parent is BinaryExpressionSyntax bin &&
                current == bin.Right &&
                bin.Kind() is SyntaxKind.LogicalAndExpression
                    or SyntaxKind.LogicalOrExpression
                    or SyntaxKind.CoalesceExpression)
            {
                return false;
            }

            if (parent is AssignmentExpressionSyntax assign &&
                current == assign.Right &&
                assign.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            {
                return false;
            }

            current = parent;
        }

        return true;
    }
}
