using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Transformation 3: Inlines a pure, non-recursive <c>[GpuDeviceFunction]</c> call at its call site
/// when the call target symbol and argument purity are verified via <see cref="SemanticModel"/>.
/// </summary>
internal static class MetamorphicDeviceFunctionInliner
{
    internal static CompilationUnitSyntax Apply(
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

        var rng = new FuzzRandom(seed ^ 0xA11CE004UL);
        var gidSymbol = MetamorphicEffectAnalyzer.ResolveGuardedGidSymbol(method, semanticModel);
        var safeReadOnlyArrays = MetamorphicEffectAnalyzer.CollectSafeReadOnlyInputArrays(method, semanticModel);

        var helperMap = new Dictionary<IMethodSymbol, (MethodDeclarationSyntax Decl, string ReturnType, string ReturnTemplate)>(
            SymbolEqualityComparer.Default);

        foreach (var helper in FuzzSyntaxHelpers.GetDeviceFunctionMethods(root))
        {
            if (MetamorphicTypeResolver.HasSemanticErrorsInMethod(helper, semanticModel) ||
                semanticModel.GetDeclaredSymbol(helper) is not IMethodSymbol helperSymbol ||
                !MetamorphicTypeResolver.IsSupportedScalarOrEnumValueType(helperSymbol.ReturnType) ||
                !HasOnlySupportedByValueParameters(helperSymbol) ||
                MetamorphicEffectAnalyzer.ContainsBarrierOrWarpCall(helper, semanticModel, root))
            {
                continue;
            }

            if (TryExtractPureReturnExpression(helper, semanticModel, out string? returnTemplate))
            {
                string retTypeText = MetamorphicTypeResolver.FormatCSharpType(helperSymbol.ReturnType);
                helperMap[helperSymbol] = (helper, retTypeText, returnTemplate);
            }
        }

        if (helperMap.Count == 0)
        {
            return root;
        }

        var eligibleCalls = new List<(InvocationExpressionSyntax Invocation, IMethodSymbol HelperSymbol, string ReturnType, string ReturnTemplate)>();
        foreach (var node in method.Body.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax inv)
            {
                continue;
            }

            if (semanticModel.GetSymbolInfo(inv).Symbol is not IMethodSymbol targetSymbol ||
                !helperMap.TryGetValue(targetSymbol, out var info))
            {
                continue;
            }

            if (inv.ArgumentList.Arguments.Count != targetSymbol.Parameters.Length ||
                !AreAllArgumentsPure(inv, semanticModel, safeReadOnlyArrays, gidSymbol))
            {
                continue;
            }

            eligibleCalls.Add((inv, targetSymbol, info.ReturnType, info.ReturnTemplate));
        }

        if (eligibleCalls.Count == 0)
        {
            return root;
        }

        var chosen = eligibleCalls[rng.NextBelow(eligibleCalls.Count)];
        int paramCount = chosen.HelperSymbol.Parameters.Length;
        var paramNames = new string[paramCount];
        var paramTypes = new string[paramCount];
        var argExprs = new string[paramCount];

        for (int i = 0; i < paramCount; i++)
        {
            var pSym = chosen.HelperSymbol.Parameters[i];
            paramNames[i] = pSym.Name;
            paramTypes[i] = MetamorphicTypeResolver.FormatCSharpType(pSym.Type);
            argExprs[i] = FuzzSyntaxHelpers.RenderNodeText(chosen.Invocation.ArgumentList.Arguments[i].Expression);
        }

        string substituted = SubstituteParameters(chosen.ReturnTemplate, paramNames, paramTypes, argExprs);
        var replacementExpr = SyntaxFactory.ParseExpression($"(({chosen.ReturnType})({substituted}))")
            .WithTriviaFrom(chosen.Invocation);

        var updatedMethod = method.ReplaceNode(chosen.Invocation, replacementExpr);
        return root.ReplaceNode(method, updatedMethod);
    }

    private static bool HasOnlySupportedByValueParameters(IMethodSymbol helperSymbol)
    {
        foreach (var p in helperSymbol.Parameters)
        {
            if (p.RefKind != RefKind.None || !MetamorphicTypeResolver.IsSupportedScalarOrEnumValueType(p.Type))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreAllArgumentsPure(
        InvocationExpressionSyntax inv,
        SemanticModel semanticModel,
        IReadOnlySet<ISymbol> safeReadOnlyArrays,
        ISymbol? gidSymbol)
    {
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (!arg.RefKindKeyword.IsKind(SyntaxKind.None) ||
                !MetamorphicEffectAnalyzer.IsPureExpression(arg.Expression, semanticModel, safeReadOnlyArrays, gidSymbol))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryExtractPureReturnExpression(
        MethodDeclarationSyntax helper,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out string? returnTemplate)
    {
        returnTemplate = null;
        var emptyArrays = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        if (helper.ExpressionBody is not null)
        {
            if (!MetamorphicEffectAnalyzer.IsPureExpression(helper.ExpressionBody.Expression, semanticModel, emptyArrays, gidSymbol: null))
            {
                return false;
            }

            returnTemplate = FuzzSyntaxHelpers.RenderNodeText(helper.ExpressionBody.Expression);
            return true;
        }

        if (helper.Body is null)
        {
            return false;
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var stmt in helper.Body.Statements)
        {
            if (stmt is LocalDeclarationStatementSyntax localDecl && localDecl.Declaration.Type is not null)
            {
                if (localDecl.Declaration.Type is RefTypeSyntax ||
                    FuzzSyntaxHelpers.IsStackAllocSpanDeclaration(localDecl))
                {
                    return false;
                }

                foreach (var v in localDecl.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(v) is not ILocalSymbol localSym ||
                        !MetamorphicTypeResolver.IsSupportedScalarOrEnumValueType(localSym.Type))
                    {
                        return false;
                    }

                    string typeText = MetamorphicTypeResolver.FormatCSharpType(localSym.Type);
                    string vName = localSym.Name;
                    string initText;
                    if (v.Initializer is not null)
                    {
                        if (!MetamorphicEffectAnalyzer.IsPureExpression(v.Initializer.Value, semanticModel, emptyArrays, gidSymbol: null))
                        {
                            return false;
                        }

                        initText = SubstituteUsingMap(FuzzSyntaxHelpers.RenderNodeText(v.Initializer.Value), env);
                    }
                    else
                    {
                        initText = $"default({typeText})";
                    }

                    env[vName] = $"(({typeText})({initText}))";
                }
            }
            else if (stmt is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assign } &&
                     assign.Left is IdentifierNameSyntax idLeft)
            {
                if (assign.Kind() is SyntaxKind.DivideAssignmentExpression or SyntaxKind.ModuloAssignmentExpression ||
                    !MetamorphicEffectAnalyzer.IsPureExpression(assign.Right, semanticModel, emptyArrays, gidSymbol: null))
                {
                    return false;
                }

                var leftSym = semanticModel.GetSymbolInfo(idLeft).Symbol;
                if (!MetamorphicTypeResolver.TryResolveSupportedSymbolValueType(leftSym, out _, out string? targetTypeText))
                {
                    return false;
                }

                string targetName = idLeft.Identifier.ValueText;
                string rhsText = SubstituteUsingMap(FuzzSyntaxHelpers.RenderNodeText(assign.Right), env);
                if (assign.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    env[targetName] = $"(({targetTypeText})({rhsText}))";
                }
                else
                {
                    string prev = env.TryGetValue(targetName, out string? existing) ? existing : targetName;
                    string binOp = assign.OperatorToken.ValueText[..^1];
                    env[targetName] = $"(({targetTypeText})(({prev}) {binOp} ({rhsText})))";
                }
            }
            else if (stmt is ReturnStatementSyntax { Expression: { } retExpr })
            {
                if (!MetamorphicEffectAnalyzer.IsPureExpression(retExpr, semanticModel, emptyArrays, gidSymbol: null))
                {
                    return false;
                }

                returnTemplate = SubstituteUsingMap(FuzzSyntaxHelpers.RenderNodeText(retExpr), env);
                return true;
            }
            else
            {
                return false;
            }
        }

        return false;
    }

    private static string SubstituteParameters(
        string template,
        IReadOnlyList<string> paramNames,
        IReadOnlyList<string> paramTypes,
        IReadOnlyList<string> argExprs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < paramNames.Count; i++)
        {
            map[paramNames[i]] = $"(({paramTypes[i]})({argExprs[i]}))";
        }

        return SubstituteUsingMap(template, map);
    }

    private static string SubstituteUsingMap(string exprText, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0)
        {
            return exprText;
        }

        var expr = SyntaxFactory.ParseExpression(exprText);
        var ids = new List<IdentifierNameSyntax>();
        foreach (var node in expr.DescendantNodesAndSelf())
        {
            if (node is IdentifierNameSyntax id &&
                !FuzzSyntaxHelpers.IsMemberAccessRight(id) &&
                map.ContainsKey(id.Identifier.ValueText))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return exprText;
        }

        var replaced = expr.ReplaceNodes(ids, (orig, _) =>
            SyntaxFactory.ParseExpression(map[orig.Identifier.ValueText]).WithTriviaFrom(orig));
        return FuzzSyntaxHelpers.RenderFullNode(replaced);
    }
}
