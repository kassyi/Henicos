#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Runs the comparisons of the null and call decisions of one method: against the syntax rules before the split, and
/// between the decisions before and after the split (#1561).
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

    /// <summary>
    /// Splits <paramref name="built"/> and compares the decisions of the nodes the split kept with those before it;
    /// the number of nodes compared, or -1 when the method has no split.
    /// </summary>
    public static int ComparePostSplit(Compilation compilation, IMethodSymbol symbol, IrMethod built,
        IrNullAgreementCounts counts)
    {
        if (symbol.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef ||
            IrSplitLowering.Apply(IrClassifier.Classify(built, compilation), compilation, out _) is not { } lowered)
        {
            return -1;
        }

        var isDevice = !symbol.HasAttribute(KnownAttributes.GpuKernel);
        var linear = IrLinearizer.Linearize(lowered, compilation);
        var machine = IrStateMachineBuilder.Build(linear, isDevice, built.Body.Source, compilation);
        var where = IrCoverageInputs.Where(syntaxRef.SyntaxTree, symbol);

        var preDecided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        var postDecided = IrDecisions.Decide(machine, IrDecisionTestContext.Create(compilation, machine, built));
        foreach (var violation in IrDecisionInvariants.Verify(postDecided))
        {
            counts.Mismatches.Add($"{where}: post-split invariant: {violation}");
        }

        var preByKey = CollectUniqueNonSynthesized(preDecided.Body);
        var compared = 0;
        foreach (var node in IrNodeChildren.DescendantsAndSelf(postDecided))
        {
            if (node is not IrExpression postExpr || postExpr.Source.Synthesized || postExpr.Decision is null)
            {
                continue;
            }

            var key = (postExpr.Source.TreeOrdinal, postExpr.Source.Start, postExpr.Source.Length,
                postExpr.GetType());
            if (!preByKey.TryGetValue(key, out var preExpr) || preExpr is null)
            {
                continue;
            }

            compared++;
            IrNullPostSplitComparer.ComparePreAndPost(preExpr, postExpr, where, counts);
        }

        return compared;
    }

    private static Dictionary<(int TreeOrdinal, int Start, int Length, Type Kind), IrExpression?>
        CollectUniqueNonSynthesized(IrNode root)
    {
        var map = new Dictionary<(int TreeOrdinal, int Start, int Length, Type Kind), IrExpression?>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(root))
        {
            if (node is not IrExpression expr || expr.Source.Synthesized || expr.Decision is null)
            {
                continue;
            }

            var key = (expr.Source.TreeOrdinal, expr.Source.Start, expr.Source.Length, expr.GetType());
            if (!map.TryAdd(key, expr))
            {
                map[key] = null;
            }
        }

        return map;
    }
}
