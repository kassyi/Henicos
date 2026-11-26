#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Runs the comparisons of the null and call decisions of one method against the syntax rules before the split (#1561).
/// </summary>
internal static class IrNullAgreementRunner
{
    /// <summary>Compares every decided node of <paramref name="built"/> with the syntax rules of its source.</summary>
    public static void ComparePreSplit(Compilation compilation, IMethodSymbol symbol, IrMethod built,
        IrNullAgreementCounts counts)
    {
        if (symbol.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef)
        {
            return;
        }

        var tree = syntaxRef.SyntaxTree;
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var where = IrCoverageInputs.Where(tree, symbol);
        var frameSymbols = IrNullAgreementFrameTracker.CollectLegacyFrameSymbols(symbol, model);

        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        foreach (var violation in IrDecisionInvariants.Verify(decided.Body))
        {
            counts.Mismatches.Add($"{where}: invariant: {violation}");
        }

        foreach (var node in IrNodeChildren.DescendantsAndSelf(decided.Body))
        {
            if (node is not IrExpression expression || expression.Source.Synthesized ||
                IrInvariants.ContainsOpaque(expression))
            {
                continue;
            }

            if (IrClassificationEquivalence.FindExpression(root, expression.Source.Span) is not { } syntax)
            {
                counts.AddExcluded($"NoSyntaxMatch.{expression.GetType().Name}");
                continue;
            }

            IrNullAgreementComparer.CompareNode(expression, syntax, model, compilation, frameSymbols, where, counts);
        }
    }
}
