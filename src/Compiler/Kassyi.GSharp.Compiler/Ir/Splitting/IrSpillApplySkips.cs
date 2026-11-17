using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Decides whether <see cref="IrSpillRewriter"/> rewrites a planned statement or leaves it to the syntax-based
/// lowering (<see cref="IrSpillApplySkip"/>), before anything is rewritten.
/// </summary>
internal static class IrSpillApplySkips
{
    /// <summary>
    /// Whether to skip <paramref name="plan"/> (<see cref="IrSpillApplySkip.LoopCondition"/> for a
    /// <see langword="while"/> loop; otherwise the first reason in the order of the steps); none when it is
    /// rewritten.
    /// </summary>
    public static IrSpillApplySkip Of(IrStatementSpillPlan plan) =>
        plan.Statement is IrWhile ? IrSpillApplySkip.LoopCondition : OfSteps(plan);

    /// <summary>
    /// Whether to skip the steps of <paramref name="plan"/> in their order without treating a <see langword="while"/>
    /// loop condition as a skip.
    /// </summary>
    public static IrSpillApplySkip OfSteps(IrStatementSpillPlan plan)
    {
        var receivers = ExtensionReceiversOfSplits(plan.Statement);
        return Of(plan.Steps, receivers);
    }

    private static IrSpillApplySkip Of(ImmutableArray<IrSpillStep> steps, List<IrExpression> receivers)
    {
        foreach (var step in steps)
        {
            var skip = step switch
            {
                IrSpillConditional { Node: IrCoalesce } => IrSpillApplySkip.CoalesceHoldsSplit,
                IrSpillConditional conditional => OfBranches(conditional, receivers),
                IrSpillValue { Node: IrConversion { IsImplicit: true, Conversion.Method: not null } } =>
                    IrSpillApplySkip.UserDefinedImplicitConversionSpill,
                IrSpillValue { Role: IrSpillRole.SplitReceiver } or IrSpillCheck
                    when IsBelowAny(step.Node, receivers) => IrSpillApplySkip.SplitExtensionReceiver,
                _ when HasNoSyntax(step.Node) => IrSpillApplySkip.SynthesizedOperand,
                _ => IrSpillApplySkip.None
            };
            if (skip != IrSpillApplySkip.None)
            {
                return skip;
            }
        }

        return IrSpillApplySkip.None;
    }

    private static IrSpillApplySkip OfBranches(IrSpillConditional conditional, List<IrExpression> receivers)
    {
        foreach (var branch in conditional.Branches)
        {
            var skip = Of(branch, receivers);
            if (skip != IrSpillApplySkip.None)
            {
                return skip;
            }
        }

        return IrSpillApplySkip.None;
    }

    // The receivers of the extension methods that split and are called on them (x.M())
    private static List<IrExpression> ExtensionReceiversOfSplits(IrStatement statement)
    {
        var receivers = new List<IrExpression>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(statement))
        {
            if (node is IrCall { Classification: { IsClassified: true, Split.IsSplit: true } } call)
            {
                call.Arguments
                    .AsStackEnumerable()
                    .Where(static argument => argument.Kind == IrArgumentKind.ExtensionReceiver)
                    .Select(static argument => argument.Value)
                    .AddTo(receivers);
            }
        }

        return receivers;
    }

    // [WHY] An implicit conversion without an operator has the syntax of its operand, and the emitter writes it from
    // the context; any other node made for no syntax (a default argument, a params array) has none to copy
    private static bool HasNoSyntax(IrExpression node) =>
        node.Source.Synthesized && node is not IrConversion { IsImplicit: true, Conversion.Method: null };

    private static bool IsBelowAny(IrExpression node, List<IrExpression> receivers) =>
        receivers
            .AsStackEnumerable()
            .Any(node, static (receiver, target) => IrNodeChildren.Exists(receiver, child => child == target));
}
