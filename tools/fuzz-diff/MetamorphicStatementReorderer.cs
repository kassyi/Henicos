using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Reorders two adjacent independent statements whose read and write symbol sets
/// are disjoint and which contain no array writes, barriers, warp calls, or side-effecting invocations.
/// </summary>
internal static class MetamorphicStatementReorderer
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

        var rng = new FuzzRandom(seed ^ 0xA11CE005UL);
        var scope = EffectScope.ForKernel(method, semanticModel);
        var statements = method.Body.Statements;

        var swappableIndices = new List<int>();
        for (int i = 0; i + 1 < statements.Count; i++)
        {
            if (MetamorphicStatementEffects.AreIndependent(statements[i], statements[i + 1], scope, root))
            {
                swappableIndices.Add(i);
            }
        }

        if (swappableIndices.Count == 0)
        {
            return root;
        }

        int chosenIdx = swappableIndices[rng.NextBelow(swappableIndices.Count)];
        var reordered = new List<StatementSyntax>(statements.Count);
        for (int i = 0; i < statements.Count; i++)
        {
            if (i == chosenIdx)
            {
                reordered.Add(statements[i + 1]);
                reordered.Add(statements[i]);
                i++;
            }
            else
            {
                reordered.Add(statements[i]);
            }
        }

        var updatedMethod = method.WithBody(method.Body.WithStatements(SyntaxFactory.List(reordered)));
        return root.ReplaceNode(method, updatedMethod);
    }
}
