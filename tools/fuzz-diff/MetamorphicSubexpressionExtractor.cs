using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Transformation 2 (a &amp; b): Extracts a pure, unconditionally-evaluated subexpression
/// to either a fresh local variable or a new <c>[GpuDeviceFunction]</c> helper method,
/// resolving types and purity via <see cref="SemanticModel"/>.
/// </summary>
internal static class MetamorphicSubexpressionExtractor
{
    internal static CompilationUnitSyntax ExtractToLocal(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        ulong seed,
        string? targetMethodName)
    {
        var method = MetamorphicEffectAnalyzer.FindTargetKernelMethod(root, targetMethodName);
        if (method?.Body is null || MetamorphicTypeResolver.HasSemanticErrorsInMethod(method, semanticModel))
        {
            return root;
        }

        var rng = new FuzzRandom(seed ^ 0xA11CE002UL);
        var usedNames = MetamorphicEffectAnalyzer.CollectAllIdentifiersInCompilationUnit(root);
        var gidSymbol = MetamorphicEffectAnalyzer.ResolveGuardedGidSymbol(method, semanticModel);
        var safeReadOnlyArrays = MetamorphicEffectAnalyzer.CollectSafeReadOnlyInputArrays(method, semanticModel);
        var statements = method.Body.Statements;

        var candidates = new List<(int StmtIndex, ExpressionSyntax SubExpr, string TypeName)>();
        for (int i = 0; i < statements.Count; i++)
        {
            var stmt = statements[i];
            if (!MetamorphicEffectAnalyzer.TryGetTopLevelPureRhsExpression(
                    stmt,
                    semanticModel,
                    safeReadOnlyArrays,
                    gidSymbol,
                    out var rhsRoot))
            {
                continue;
            }

            foreach (var node in rhsRoot.DescendantNodes())
            {
                if (node is not ExpressionSyntax subExpr ||
                    subExpr is ParenthesizedExpressionSyntax ||
                    !IsNonTrivialExtractableExpression(subExpr))
                {
                    continue;
                }

                if (!MetamorphicEffectAnalyzer.IsOnUnconditionalEvaluationPath(subExpr, rhsRoot) ||
                    !MetamorphicEffectAnalyzer.IsPureExpression(subExpr, semanticModel, safeReadOnlyArrays, gidSymbol) ||
                    !MetamorphicTypeResolver.TryResolveSupportedValueType(subExpr, semanticModel, out _, out string? exprType))
                {
                    continue;
                }

                candidates.Add((i, subExpr, exprType));
            }
        }

        if (candidates.Count == 0)
        {
            return root;
        }

        var chosen = candidates[rng.NextBelow(candidates.Count)];
        string newLocalName = MetamorphicEffectAnalyzer.AllocateFreshIdentifier("metaExtracted", usedNames);
        string subExprText = FuzzSyntaxHelpers.RenderNodeText(chosen.SubExpr);

        var tempDeclStmt = SyntaxFactory.ParseStatement($"{chosen.TypeName} {newLocalName} = ({subExprText});\n");
        var replacementId = SyntaxFactory.IdentifierName(newLocalName).WithTriviaFrom(chosen.SubExpr);
        var updatedStmt = statements[chosen.StmtIndex].ReplaceNode(chosen.SubExpr, replacementId);

        var newStatements = new List<StatementSyntax>(statements.Count + 1);
        for (int i = 0; i < statements.Count; i++)
        {
            if (i == chosen.StmtIndex)
            {
                newStatements.Add(tempDeclStmt);
                newStatements.Add(updatedStmt);
            }
            else
            {
                newStatements.Add(statements[i]);
            }
        }

        var updatedMethod = method.WithBody(method.Body.WithStatements(SyntaxFactory.List(newStatements)));
        return root.ReplaceNode(method, updatedMethod);
    }

    internal static CompilationUnitSyntax ExtractToDeviceFunction(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        ulong seed,
        string? targetMethodName,
        string? helperMethodName)
    {
        var method = MetamorphicEffectAnalyzer.FindTargetKernelMethod(root, targetMethodName);
        if (method?.Body is null ||
            method.Parent is not ClassDeclarationSyntax parentClass ||
            MetamorphicTypeResolver.HasSemanticErrorsInMethod(method, semanticModel))
        {
            return root;
        }

        var rng = new FuzzRandom(seed ^ 0xA11CE003UL);
        var usedNames = MetamorphicEffectAnalyzer.CollectAllIdentifiersInCompilationUnit(root);
        var gidSymbol = MetamorphicEffectAnalyzer.ResolveGuardedGidSymbol(method, semanticModel);
        var safeReadOnlyArrays = MetamorphicEffectAnalyzer.CollectSafeReadOnlyInputArrays(method, semanticModel);
        var statements = method.Body.Statements;

        var candidates = new List<(int StmtIndex, ExpressionSyntax SubExpr, string ReturnType, List<(string Name, string Type)> FreeVars)>();
        for (int i = 0; i < statements.Count; i++)
        {
            var stmt = statements[i];
            if (!MetamorphicEffectAnalyzer.TryGetTopLevelPureRhsExpression(
                    stmt,
                    semanticModel,
                    safeReadOnlyArrays,
                    gidSymbol,
                    out var rhsRoot))
            {
                continue;
            }

            foreach (var node in rhsRoot.DescendantNodes())
            {
                if (node is not ExpressionSyntax subExpr ||
                    subExpr is ParenthesizedExpressionSyntax ||
                    !IsNonTrivialExtractableExpression(subExpr) ||
                    ContainsElementAccess(subExpr))
                {
                    continue;
                }

                if (!MetamorphicEffectAnalyzer.IsOnUnconditionalEvaluationPath(subExpr, rhsRoot) ||
                    !MetamorphicEffectAnalyzer.IsPureExpression(subExpr, semanticModel, safeReadOnlyArrays, gidSymbol) ||
                    !MetamorphicTypeResolver.TryResolveSupportedValueType(subExpr, semanticModel, out _, out string? returnType) ||
                    !TryCollectValueTypeFreeVariables(subExpr, semanticModel, out var freeVars) ||
                    freeVars.Count is < 1 or > 4)
                {
                    continue;
                }

                candidates.Add((i, subExpr, returnType, freeVars));
            }
        }

        if (candidates.Count == 0)
        {
            return root;
        }

        var chosen = candidates[rng.NextBelow(candidates.Count)];
        string effectiveHelperName = !string.IsNullOrWhiteSpace(helperMethodName) && usedNames.Add(helperMethodName)
            ? helperMethodName
            : MetamorphicEffectAnalyzer.AllocateFreshIdentifier("MetaExtractedFunc_", usedNames);

        var paramDecls = new string[chosen.FreeVars.Count];
        var callArgs = new string[chosen.FreeVars.Count];
        for (int i = 0; i < chosen.FreeVars.Count; i++)
        {
            var fv = chosen.FreeVars[i];
            paramDecls[i] = $"{fv.Type} {fv.Name}";
            callArgs[i] = fv.Name;
        }

        string bodyExprText = FuzzSyntaxHelpers.RenderNodeText(chosen.SubExpr);
        string helperDeclText =
            $$"""
            [GpuDeviceFunction]
            public static {{chosen.ReturnType}} {{effectiveHelperName}}({{string.Join(", ", paramDecls)}})
            {
            return ({{chosen.ReturnType}})({{bodyExprText}});
            }
            """;

        var helperMember = SyntaxFactory.ParseMemberDeclaration(helperDeclText + "\n");
        if (helperMember is null)
        {
            return root;
        }

        var callExpr = SyntaxFactory.ParseExpression($"{effectiveHelperName}({string.Join(", ", callArgs)})")
            .WithTriviaFrom(chosen.SubExpr);
        var updatedStmt = statements[chosen.StmtIndex].ReplaceNode(chosen.SubExpr, callExpr);
        var updatedMethod = FuzzSyntaxHelpers.ReplaceSingleStatement(method, chosen.StmtIndex, updatedStmt);

        int methodMemberIdx = parentClass.Members.IndexOf(method);
        if (methodMemberIdx < 0)
        {
            return root;
        }

        var newMembers = parentClass.Members
            .Replace(method, updatedMethod)
            .Insert(methodMemberIdx, helperMember);
        var updatedClass = parentClass.WithMembers(newMembers);
        return root.ReplaceNode(parentClass, updatedClass);
    }

    private static bool IsNonTrivialExtractableExpression(ExpressionSyntax expr) =>
        expr switch
        {
            BinaryExpressionSyntax => true,
            PrefixUnaryExpressionSyntax pre => pre.Kind() is SyntaxKind.BitwiseNotExpression or SyntaxKind.UnaryMinusExpression or SyntaxKind.LogicalNotExpression,
            CastExpressionSyntax cast => cast.Expression is BinaryExpressionSyntax or ParenthesizedExpressionSyntax,
            _ => false,
        };

    private static bool ContainsElementAccess(ExpressionSyntax expr)
    {
        foreach (var node in expr.DescendantNodesAndSelf())
        {
            if (node is ElementAccessExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCollectValueTypeFreeVariables(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        out List<(string Name, string Type)> freeVars)
    {
        freeVars = [];
        var seenSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var node in expr.DescendantNodesAndSelf())
        {
            if (node is MemberAccessExpressionSyntax member)
            {
                // Allow enum member constants (e.g. FuzzEnumByte.One), reject instance field accesses on local structs.
                if (semanticModel.GetSymbolInfo(member).Symbol is not IFieldSymbol { IsConst: true, ContainingType.TypeKind: TypeKind.Enum })
                {
                    return false;
                }

                continue;
            }

            if (node is not IdentifierNameSyntax id || FuzzSyntaxHelpers.IsMemberAccessRight(id))
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(id).Symbol;
            if (symbol is null)
            {
                return false;
            }

            if (symbol is ITypeSymbol or INamespaceSymbol)
            {
                continue;
            }

            if (symbol is IFieldSymbol { IsConst: true })
            {
                continue;
            }

            if (!MetamorphicTypeResolver.TryResolveSupportedSymbolValueType(symbol, out _, out string? typeSyntaxText))
            {
                return false;
            }

            if (seenSymbols.Add(symbol))
            {
                freeVars.Add((symbol.Name, typeSyntaxText));
            }
        }

        return true;
    }
}
