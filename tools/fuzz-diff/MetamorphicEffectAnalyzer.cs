using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Semantic-model-backed purity, side-effect, statement-independence, array-aliasing,
/// and barrier/warp synchronization analysis for <see cref="FuzzMetamorphicRewriter"/>.
/// </summary>
internal static class MetamorphicEffectAnalyzer
{
    internal static MethodDeclarationSyntax? FindTargetKernelMethod(CompilationUnitSyntax root, string? targetMethodName)
    {
        var kernels = FuzzSyntaxHelpers.GetKernelMethods(root);
        if (kernels.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(targetMethodName))
        {
            foreach (var k in kernels)
            {
                if (string.Equals(k.Identifier.ValueText, targetMethodName, StringComparison.Ordinal))
                {
                    return k;
                }
            }
        }

        return kernels[0];
    }

    internal static ISymbol? ResolveGuardedGidSymbol(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        if (method.Body is null)
        {
            return null;
        }

        var statements = method.Body.Statements;
        if (statements.Count < 2 ||
            !FuzzSyntaxHelpers.IsGidDeclaration(statements[0]) ||
            !FuzzSyntaxHelpers.IsGidBoundsGuard(statements[1]) ||
            statements[0] is not LocalDeclarationStatementSyntax gidDecl)
        {
            return null;
        }

        foreach (var v in gidDecl.Declaration.Variables)
        {
            if (string.Equals(v.Identifier.ValueText, "gid", StringComparison.Ordinal))
            {
                return semanticModel.GetDeclaredSymbol(v);
            }
        }

        return null;
    }

    internal static HashSet<ISymbol> CollectSafeReadOnlyInputArrays(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel)
    {
        var candidates = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var p in method.ParameterList.Parameters)
        {
            if (string.Equals(p.Identifier.ValueText, "checksum", StringComparison.Ordinal))
            {
                continue;
            }

            if (semanticModel.GetDeclaredSymbol(p) is IParameterSymbol
                {
                    RefKind: RefKind.None,
                    Type: IArrayTypeSymbol { Rank: 1 },
                } paramSymbol)
            {
                candidates.Add(paramSymbol);
            }
        }

        if (method.Body is null || candidates.Count == 0)
        {
            return candidates;
        }

        foreach (var node in method.Body.DescendantNodes())
        {
            if (node is ElementAccessExpressionSyntax elem && IsWrittenOrByRefElementAccess(elem))
            {
                var arrSymbol = semanticModel.GetSymbolInfo(elem.Expression).Symbol;
                if (arrSymbol is not null)
                {
                    candidates.Remove(arrSymbol);
                }
                else
                {
                    candidates.Clear();
                    break;
                }
            }
        }

        return candidates;
    }

    internal static bool ContainsBarrierOrWarpCall(
        SyntaxNode node,
        SemanticModel semanticModel,
        CompilationUnitSyntax root)
    {
        var visitedMethods = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        return ContainsBarrierOrWarpCallCore(node, semanticModel, root, visitedMethods);
    }

    internal static bool IsSideEffectFreeExpression(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        IReadOnlySet<ISymbol> safeReadOnlyArrays,
        ISymbol? gidSymbol)
    {
        foreach (var node in expr.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax or
                    InvocationExpressionSyntax or
                    StackAllocArrayCreationExpressionSyntax or
                    RefExpressionSyntax or
                    DeclarationExpressionSyntax or
                    ThrowExpressionSyntax or
                    AwaitExpressionSyntax or
                    AnonymousFunctionExpressionSyntax:
                    return false;

                case PrefixUnaryExpressionSyntax prefix when prefix.Kind() is
                    SyntaxKind.PreIncrementExpression or
                    SyntaxKind.PreDecrementExpression or
                    SyntaxKind.PointerIndirectionExpression or
                    SyntaxKind.AddressOfExpression:
                    return false;

                case PostfixUnaryExpressionSyntax postfix when postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression:
                    return false;

                case BinaryExpressionSyntax bin:
                {
                    if (semanticModel.GetSymbolInfo(bin).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
                    {
                        return false;
                    }

                    break;
                }

                case MemberAccessExpressionSyntax member:
                {
                    var memberSymbol = semanticModel.GetSymbolInfo(member).Symbol;
                    if (memberSymbol is null)
                    {
                        return false;
                    }

                    if (memberSymbol is IPropertySymbol)
                    {
                        return false;
                    }

                    if (memberSymbol is IFieldSymbol field && field.IsStatic && !field.IsConst)
                    {
                        return false;
                    }

                    break;
                }

                case ElementAccessExpressionSyntax elem:
                {
                    if (!IsSafeInputElementRead(elem, semanticModel, safeReadOnlyArrays, gidSymbol))
                    {
                        return false;
                    }

                    break;
                }

                case IdentifierNameSyntax id:
                {
                    if (FuzzSyntaxHelpers.IsMemberAccessRight(id))
                    {
                        break;
                    }

                    var sym = semanticModel.GetSymbolInfo(id).Symbol;
                    if (sym is null)
                    {
                        return false;
                    }

                    if (sym is IFieldSymbol staticField && staticField.IsStatic && !staticField.IsConst)
                    {
                        return false;
                    }

                    break;
                }
            }
        }

        return true;
    }

    internal static bool IsPureExpression(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        IReadOnlySet<ISymbol> safeReadOnlyArrays,
        ISymbol? gidSymbol)
    {
        if (!MetamorphicTypeResolver.TryResolveExpressionType(expr, semanticModel, out _) ||
            !IsSideEffectFreeExpression(expr, semanticModel, safeReadOnlyArrays, gidSymbol))
        {
            return false;
        }

        foreach (var node in expr.DescendantNodesAndSelf())
        {
            if (node is BinaryExpressionSyntax bin &&
                bin.Kind() is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression)
            {
                return false;
            }

            if (node is ObjectCreationExpressionSyntax or
                ImplicitObjectCreationExpressionSyntax or
                TupleExpressionSyntax or
                SwitchExpressionSyntax or
                DefaultExpressionSyntax)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsSafeInputElementRead(
        ElementAccessExpressionSyntax elem,
        SemanticModel semanticModel,
        IReadOnlySet<ISymbol> safeReadOnlyArrays,
        ISymbol? gidSymbol)
    {
        if (gidSymbol is null ||
            IsWrittenOrByRefElementAccess(elem) ||
            elem.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        var arraySymbol = semanticModel.GetSymbolInfo(elem.Expression).Symbol;
        if (arraySymbol is null || !safeReadOnlyArrays.Contains(arraySymbol))
        {
            return false;
        }

        var indexExpr = elem.ArgumentList.Arguments[0].Expression;
        var indexSymbol = semanticModel.GetSymbolInfo(indexExpr).Symbol;
        return indexSymbol is not null && SymbolEqualityComparer.Default.Equals(indexSymbol, gidSymbol);
    }

    internal static bool TryGetTopLevelPureRhsExpression(
        StatementSyntax stmt,
        SemanticModel semanticModel,
        IReadOnlySet<ISymbol> safeReadOnlyArrays,
        ISymbol? gidSymbol,
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
            IsSideEffectFreeExpression(initVal, semanticModel, safeReadOnlyArrays, gidSymbol))
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
            semanticModel.GetSymbolInfo(leftId).Symbol is ILocalSymbol { IsRef: false } &&
            IsSideEffectFreeExpression(assignRhs, semanticModel, safeReadOnlyArrays, gidSymbol))
        {
            rhsRoot = assignRhs;
            return true;
        }

        return false;
    }

    internal static bool AreStatementsIndependent(
        StatementSyntax first,
        StatementSyntax second,
        SemanticModel semanticModel,
        CompilationUnitSyntax root,
        IReadOnlySet<ISymbol> safeReadOnlyArrays,
        ISymbol? gidSymbol)
    {
        if (ContainsBarrierOrWarpCall(first, semanticModel, root) ||
            ContainsBarrierOrWarpCall(second, semanticModel, root))
        {
            return false;
        }

        if (!TryGetStatementEffects(first, semanticModel, safeReadOnlyArrays, gidSymbol, out var r1, out var w1) ||
            !TryGetStatementEffects(second, semanticModel, safeReadOnlyArrays, gidSymbol, out var r2, out var w2))
        {
            return false;
        }

        return !w1.Overlaps(r2) &&
               !w1.Overlaps(w2) &&
               !w2.Overlaps(r1) &&
               !w2.Overlaps(w1);
    }

    internal static bool TryGetStatementEffects(
        StatementSyntax stmt,
        SemanticModel semanticModel,
        IReadOnlySet<ISymbol> safeReadOnlyArrays,
        ISymbol? gidSymbol,
        out HashSet<ISymbol> reads,
        out HashSet<ISymbol> writes)
    {
        reads = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        writes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        if (FuzzSyntaxHelpers.IsGidDeclaration(stmt) ||
            FuzzSyntaxHelpers.IsGidBoundsGuard(stmt) ||
            FuzzSyntaxHelpers.IsHashInitDeclaration(stmt) ||
            FuzzSyntaxHelpers.IsFinalChecksumWrite(stmt))
        {
            return false;
        }

        if (stmt is LocalDeclarationStatementSyntax localDecl)
        {
            if (localDecl.Declaration.Type is RefTypeSyntax ||
                FuzzSyntaxHelpers.IsStackAllocSpanDeclaration(localDecl))
            {
                return false;
            }

            foreach (var v in localDecl.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(v) is not ILocalSymbol { IsRef: false } localSymbol ||
                    localSymbol.Type.TypeKind == TypeKind.Error)
                {
                    return false;
                }

                writes.Add(localSymbol);
                if (v.Initializer is not null &&
                    !TryCollectExpressionEffects(v.Initializer.Value, semanticModel, safeReadOnlyArrays, gidSymbol, reads, writes))
                {
                    return false;
                }
            }

            return true;
        }

        if (stmt is ExpressionStatementSyntax exprStmt)
        {
            return TryCollectExpressionEffects(exprStmt.Expression, semanticModel, safeReadOnlyArrays, gidSymbol, reads, writes);
        }

        return false;
    }

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

    internal static HashSet<string> CollectAllIdentifiersInCompilationUnit(CompilationUnitSyntax root)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in root.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                set.Add(token.ValueText);
            }
        }

        return set;
    }

    internal static string AllocateFreshIdentifier(string prefix, HashSet<string> usedNames)
    {
        for (int index = 0; ; index++)
        {
            string candidate = string.Create(CultureInfo.InvariantCulture, $"{prefix}{index}");
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool TryCollectExpressionEffects(
        ExpressionSyntax rootExpr,
        SemanticModel semanticModel,
        IReadOnlySet<ISymbol> safeReadOnlyArrays,
        ISymbol? gidSymbol,
        HashSet<ISymbol> reads,
        HashSet<ISymbol> writes)
    {
        foreach (var node in rootExpr.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case InvocationExpressionSyntax or
                    StackAllocArrayCreationExpressionSyntax or
                    RefExpressionSyntax or
                    ThrowExpressionSyntax:
                    return false;

                case PrefixUnaryExpressionSyntax pointerPrefix when pointerPrefix.Kind() is
                    SyntaxKind.PointerIndirectionExpression or
                    SyntaxKind.AddressOfExpression:
                    return false;

                case BinaryExpressionSyntax bin:
                {
                    if (bin.Kind() is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression)
                    {
                        return false;
                    }

                    if (semanticModel.GetSymbolInfo(bin).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
                    {
                        return false;
                    }

                    break;
                }

                case AssignmentExpressionSyntax assign:
                {
                    if (assign.Kind() is SyntaxKind.DivideAssignmentExpression or SyntaxKind.ModuloAssignmentExpression)
                    {
                        return false;
                    }

                    if (!TryRecordAssignmentTarget(
                            assign.Left,
                            isCompound: !assign.IsKind(SyntaxKind.SimpleAssignmentExpression),
                            semanticModel,
                            reads,
                            writes))
                    {
                        return false;
                    }

                    break;
                }

                case PrefixUnaryExpressionSyntax prefix when prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression:
                {
                    if (!TryUnwrapRootStorageSymbol(prefix.Operand, semanticModel, out var preTarget))
                    {
                        return false;
                    }

                    reads.Add(preTarget);
                    writes.Add(preTarget);
                    break;
                }

                case PostfixUnaryExpressionSyntax postfix when postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression:
                {
                    if (!TryUnwrapRootStorageSymbol(postfix.Operand, semanticModel, out var postTarget))
                    {
                        return false;
                    }

                    reads.Add(postTarget);
                    writes.Add(postTarget);
                    break;
                }

                case MemberAccessExpressionSyntax member:
                {
                    var memberSymbol = semanticModel.GetSymbolInfo(member).Symbol;
                    if (memberSymbol is null or IPropertySymbol)
                    {
                        return false;
                    }

                    if (memberSymbol is IFieldSymbol field && field.IsStatic && !field.IsConst)
                    {
                        return false;
                    }

                    break;
                }

                case ElementAccessExpressionSyntax elem:
                {
                    // Any write or by-ref access to an array element is rejected as a reordering wall
                    // because distinct array parameters may alias the same underlying array buffer.
                    if (!IsSafeInputElementRead(elem, semanticModel, safeReadOnlyArrays, gidSymbol))
                    {
                        return false;
                    }

                    break;
                }

                case IdentifierNameSyntax id:
                {
                    if (FuzzSyntaxHelpers.IsMemberAccessRight(id))
                    {
                        break;
                    }

                    var sym = semanticModel.GetSymbolInfo(id).Symbol;
                    if (sym is null)
                    {
                        return false;
                    }

                    if (sym is ITypeSymbol or INamespaceSymbol)
                    {
                        break;
                    }

                    if (sym is IFieldSymbol fieldSymbol)
                    {
                        if (fieldSymbol.IsStatic && !fieldSymbol.IsConst)
                        {
                            return false;
                        }

                        break;
                    }

                    if (sym is ILocalSymbol or IParameterSymbol)
                    {
                        if (id.Parent is AssignmentExpressionSyntax simpleAssign &&
                            simpleAssign.Left == id &&
                            simpleAssign.IsKind(SyntaxKind.SimpleAssignmentExpression))
                        {
                            break;
                        }

                        reads.Add(sym);
                        break;
                    }

                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryRecordAssignmentTarget(
        ExpressionSyntax left,
        bool isCompound,
        SemanticModel semanticModel,
        HashSet<ISymbol> reads,
        HashSet<ISymbol> writes)
    {
        if (left is IdentifierNameSyntax id)
        {
            if (!IsMutableValueSymbol(semanticModel.GetSymbolInfo(id).Symbol, out var targetSymbol))
            {
                return false;
            }

            writes.Add(targetSymbol);
            if (isCompound)
            {
                reads.Add(targetSymbol);
            }

            return true;
        }

        if (left is MemberAccessExpressionSyntax member &&
            semanticModel.GetSymbolInfo(member).Symbol is IFieldSymbol { IsStatic: false } &&
            TryUnwrapRootStorageSymbol(member, semanticModel, out var rootSymbol))
        {
            reads.Add(rootSymbol);
            writes.Add(rootSymbol);
            return true;
        }

        return false;
    }

    private static bool TryUnwrapRootStorageSymbol(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out ISymbol? storageSymbol)
    {
        ExpressionSyntax current = expr;
        while (current is ParenthesizedExpressionSyntax paren)
        {
            current = paren.Expression;
        }

        while (current is MemberAccessExpressionSyntax member)
        {
            if (semanticModel.GetSymbolInfo(member).Symbol is not IFieldSymbol { IsStatic: false })
            {
                storageSymbol = null;
                return false;
            }

            current = member.Expression;
            while (current is ParenthesizedExpressionSyntax innerParen)
            {
                current = innerParen.Expression;
            }
        }

        if (current is IdentifierNameSyntax id &&
            IsMutableValueSymbol(semanticModel.GetSymbolInfo(id).Symbol, out storageSymbol))
        {
            return true;
        }

        storageSymbol = null;
        return false;
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

    private static bool IsWrittenOrByRefElementAccess(ElementAccessExpressionSyntax elem) =>
        elem.Parent switch
        {
            AssignmentExpressionSyntax assign => assign.Left == elem,
            PrefixUnaryExpressionSyntax prefix => prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression or SyntaxKind.AddressOfExpression,
            PostfixUnaryExpressionSyntax postfix => postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression,
            ArgumentSyntax arg => !arg.RefKindKeyword.IsKind(SyntaxKind.None),
            RefExpressionSyntax => true,
            _ => false,
        };

    private static bool ContainsBarrierOrWarpCallCore(
        SyntaxNode node,
        SemanticModel semanticModel,
        CompilationUnitSyntax root,
        HashSet<ISymbol> visitedMethods)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            if (descendant is not InvocationExpressionSyntax inv)
            {
                continue;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(inv);
            var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
            if (methodSymbol is null)
            {
                // Fail-closed: if the target method cannot be resolved by SemanticModel, treat it as a potential barrier/synchronization call.
                return true;
            }

            if (IsGpuSynchronizationOrIntrinsicSymbol(methodSymbol))
            {
                return true;
            }

            if (visitedMethods.Add(methodSymbol))
            {
                foreach (var syntaxRef in methodSymbol.DeclaringSyntaxReferences)
                {
                    if (syntaxRef.SyntaxTree == root.SyntaxTree &&
                        syntaxRef.GetSyntax() is MethodDeclarationSyntax targetDecl)
                    {
                        SyntaxNode? bodyNode = (SyntaxNode?)targetDecl.Body ?? targetDecl.ExpressionBody;
                        if (bodyNode is not null &&
                            ContainsBarrierOrWarpCallCore(bodyNode, semanticModel, root, visitedMethods))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool IsGpuSynchronizationOrIntrinsicSymbol(IMethodSymbol methodSymbol)
    {
        string methodName = methodSymbol.Name;
        if (methodName.StartsWith("Sync", StringComparison.Ordinal) ||
            methodName.StartsWith("ThreadFence", StringComparison.Ordinal) ||
            methodName.StartsWith("Shfl", StringComparison.Ordinal) ||
            methodName.StartsWith("Ballot", StringComparison.Ordinal) ||
            methodName.StartsWith("Atomic", StringComparison.Ordinal))
        {
            return true;
        }

        for (INamedTypeSymbol? type = methodSymbol.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.Name is "Gpu" or "Warp" or "GpuAtomic" or "Interlocked")
            {
                return true;
            }
        }

        return IsInGSharpCoreNamespace(methodSymbol.ContainingNamespace);
    }

    private static bool IsInGSharpCoreNamespace(INamespaceSymbol? ns)
    {
        for (INamespaceSymbol? current = ns; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
        {
            if (string.Equals(current.Name, "Core", StringComparison.Ordinal) &&
                current.ContainingNamespace is { Name: "GSharp", ContainingNamespace.Name: "Kassyi" })
            {
                return true;
            }
        }

        return false;
    }
}
