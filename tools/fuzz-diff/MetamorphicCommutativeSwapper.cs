using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Swaps the left and right operands of a commutative binary operator
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
        var method = MetamorphicTarget.FindKernel(root, targetMethodName);
        if (method?.Body is null || MetamorphicTypeResolver.HasSemanticErrorsInMethod(method, semanticModel))
        {
            return root;
        }

        var candidates = CollectSwappableCandidates(method, semanticModel);
        if (candidates.Count == 0)
        {
            return root;
        }

        var rng = new FuzzRandom(seed ^ 0xA11CE006UL);
        var chosen = candidates[rng.NextBelow(candidates.Count)];
        var swapped = chosen
            .WithLeft(chosen.Right.WithoutTrivia().WithTriviaFrom(chosen.Left))
            .WithRight(chosen.Left.WithoutTrivia().WithTriviaFrom(chosen.Right));

        var updatedMethod = method.ReplaceNode(chosen, swapped);
        return root.ReplaceNode(method, updatedMethod);
    }

    private static List<BinaryExpressionSyntax> CollectSwappableCandidates(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel)
    {
        var scope = EffectScope.ForKernel(method, semanticModel);
        var candidates = new List<BinaryExpressionSyntax>();

        foreach (var stmt in method.Body!.Statements)
        {
            if (!MetamorphicPurity.TryGetTopLevelPureRhsExpression(stmt, scope, out var rhsRoot))
            {
                continue;
            }

            foreach (var node in rhsRoot.DescendantNodesAndSelf())
            {
                if (node is BinaryExpressionSyntax bin &&
                    IsSwappableBinaryExpression(bin, scope, semanticModel))
                {
                    candidates.Add(bin);
                }
            }
        }

        return candidates;
    }

    private static bool IsSwappableBinaryExpression(
        BinaryExpressionSyntax bin,
        EffectScope scope,
        SemanticModel semanticModel)
    {
        if (!IsCommutativeBinaryKind(bin.Kind()))
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(bin).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
        {
            return false;
        }

        if (!MetamorphicTypeResolver.TryResolveSupportedValueType(bin.Left, semanticModel, out var leftType, out _) ||
            !MetamorphicTypeResolver.TryResolveSupportedValueType(bin.Right, semanticModel, out var rightType, out _) ||
            !MetamorphicTypeResolver.TryResolveExpressionType(bin, semanticModel, out var resultType) ||
            !SymbolEqualityComparer.Default.Equals(leftType, rightType))
        {
            return false;
        }

        // Never swap floating-point operands: when both operands are NaN with distinct payloads,
        // swapping `a + b` or `a * b` can change the propagated NaN payload bits under exact/checksum comparison.
        if (MetamorphicTypeResolver.IsFloatingPointType(leftType) ||
            MetamorphicTypeResolver.IsFloatingPointType(resultType))
        {
            return false;
        }

        if ((bin.IsKind(SyntaxKind.AddExpression) || bin.IsKind(SyntaxKind.MultiplyExpression)) &&
            (!MetamorphicTypeResolver.Is32Or64BitIntegerType(leftType) ||
             !SymbolEqualityComparer.Default.Equals(leftType, resultType)))
        {
            return false;
        }

        if (!MetamorphicPurity.IsPure(bin.Left, scope) || !MetamorphicPurity.IsPure(bin.Right, scope))
        {
            return false;
        }

        string leftText = FuzzSyntaxHelpers.RenderNodeText(bin.Left);
        string rightText = FuzzSyntaxHelpers.RenderNodeText(bin.Right);
        return !string.Equals(leftText, rightText, StringComparison.Ordinal);
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
