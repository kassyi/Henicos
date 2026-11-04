using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Inlines a pure, non-recursive <c>[GpuDeviceFunction]</c> call at its call site
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
        var method = MetamorphicTarget.FindKernel(root, targetMethodName);
        if (method?.Body is null || MetamorphicTypeResolver.HasSemanticErrorsInMethod(method, semanticModel))
        {
            return root;
        }

        var rng = new FuzzRandom(seed ^ 0xA11CE004UL);
        var scope = EffectScope.ForKernel(method, semanticModel);

        var helperMap = new Dictionary<IMethodSymbol, (MethodDeclarationSyntax Decl, string ReturnType, string ReturnTemplate)>(
            SymbolEqualityComparer.Default);

        foreach (var helper in FuzzSyntaxHelpers.GetDeviceFunctionMethods(root))
        {
            if (MetamorphicTypeResolver.HasSemanticErrorsInMethod(helper, semanticModel) ||
                semanticModel.GetDeclaredSymbol(helper) is not IMethodSymbol helperSymbol ||
                !MetamorphicTypeResolver.IsSupportedScalarOrEnumValueType(helperSymbol.ReturnType) ||
                !HasOnlySupportedByValueParameters(helperSymbol) ||
                MetamorphicBarrierDetector.ContainsBarrierOrWarpCall(helper, semanticModel, root))
            {
                continue;
            }

            if (PureBodyFolder.TryFold(helper, semanticModel, out string? returnTemplate))
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
                !AreAllArgumentsPure(inv, scope))
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

    private static bool AreAllArgumentsPure(InvocationExpressionSyntax inv, EffectScope scope)
    {
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (!arg.RefKindKeyword.IsKind(SyntaxKind.None) ||
                !MetamorphicPurity.IsPure(arg.Expression, scope))
            {
                return false;
            }
        }

        return true;
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

        return PureBodyFolder.SubstituteUsingMap(template, map);
    }
}
