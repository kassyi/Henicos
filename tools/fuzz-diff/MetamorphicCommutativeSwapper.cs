using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Transformation 5: Swaps the left and right operands of a commutative binary operator
/// (<c>+</c>, <c>*</c>, <c>&amp;</c>, <c>|</c>, <c>^</c>, <c>==</c>, <c>!=</c>) when both operands are pure,
/// share the same non-floating-point value type, and resolve to a built-in operator via <see cref="SemanticModel"/>.
/// </summary>
internal static class MetamorphicCommutativeSwapper
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

        var rng = new FuzzRandom(seed ^ 0xA11CE006UL);
        var gidSymbol = MetamorphicEffectAnalyzer.ResolveGuardedGidSymbol(method, semanticModel);
        var safeReadOnlyArrays = MetamorphicEffectAnalyzer.CollectSafeReadOnlyInputArrays(method, semanticModel);
        var candidates = new List<BinaryExpressionSyntax>();

        foreach (var stmt in method.Body.Statements)
        {
            if (!MetamorphicEffectAnalyzer.TryGetTopLevelPureRhsExpression(
                    stmt,
                    semanticModel,
                    safeReadOnlyArrays,
                    gidSymbol,
                    out var rhsRoot))
            {
                continue;
            }

            foreach (var node in rhsRoot.DescendantNodesAndSelf())
            {
                if (node is not BinaryExpressionSyntax bin || !IsCommutativeBinaryKind(bin.Kind()))
                {
                    continue;
                }

                if (semanticModel.GetSymbolInfo(bin).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
                {
                    continue;
                }

                if (!MetamorphicTypeResolver.TryResolveSupportedValueType(bin.Left, semanticModel, out var leftType, out _) ||
                    !MetamorphicTypeResolver.TryResolveSupportedValueType(bin.Right, semanticModel, out var rightType, out _) ||
                    !MetamorphicTypeResolver.TryResolveExpressionType(bin, semanticModel, out var resultType) ||
                    !SymbolEqualityComparer.Default.Equals(leftType, rightType))
                {
                    continue;
                }

                // Never swap floating-point operands: when both operands are NaN with distinct payloads,
                // swapping `a + b` or `a * b` can change the propagated NaN payload bits under exact/checksum comparison.
                if (MetamorphicTypeResolver.IsFloatingPointType(leftType) ||
                    MetamorphicTypeResolver.IsFloatingPointType(resultType))
                {
                    continue;
                }

                if ((bin.IsKind(SyntaxKind.AddExpression) || bin.IsKind(SyntaxKind.MultiplyExpression)) &&
                    (!MetamorphicTypeResolver.Is32Or64BitIntegerType(leftType) ||
                     !SymbolEqualityComparer.Default.Equals(leftType, resultType)))
                {
                    continue;
                }

                if (!MetamorphicEffectAnalyzer.IsPureExpression(bin.Left, semanticModel, safeReadOnlyArrays, gidSymbol) ||
                    !MetamorphicEffectAnalyzer.IsPureExpression(bin.Right, semanticModel, safeReadOnlyArrays, gidSymbol))
                {
                    continue;
                }

                string leftText = FuzzSyntaxHelpers.RenderNodeText(bin.Left);
                string rightText = FuzzSyntaxHelpers.RenderNodeText(bin.Right);
                if (string.Equals(leftText, rightText, StringComparison.Ordinal))
                {
                    continue;
                }

                candidates.Add(bin);
            }
        }

        if (candidates.Count == 0)
        {
            return root;
        }

        var chosen = candidates[rng.NextBelow(candidates.Count)];
        var swapped = chosen
            .WithLeft(chosen.Right.WithoutTrivia().WithTriviaFrom(chosen.Left))
            .WithRight(chosen.Left.WithoutTrivia().WithTriviaFrom(chosen.Right));

        var updatedMethod = method.ReplaceNode(chosen, swapped);
        return root.ReplaceNode(method, updatedMethod);
    }

    private static bool IsCommutativeBinaryKind(SyntaxKind kind) =>
        kind is SyntaxKind.AddExpression
            or SyntaxKind.MultiplyExpression
            or SyntaxKind.BitwiseAndExpression
            or SyntaxKind.BitwiseOrExpression
            or SyntaxKind.ExclusiveOrExpression
            or SyntaxKind.EqualsExpression
            or SyntaxKind.NotEqualsExpression;
}
