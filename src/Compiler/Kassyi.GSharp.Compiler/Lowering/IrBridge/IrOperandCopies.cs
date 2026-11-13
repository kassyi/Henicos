using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Whether the operands a plan evaluates into temporaries can be copied out of their statement as
/// <c>var t = &lt;operand&gt;;</c>, which the C# of the managed path compiles: the operand needs a type of its own,
/// and the overflow context of a <c>checked(...)</c> or <c>unchecked(...)</c> around it would not come with it.
/// </summary>
internal static class IrOperandCopies
{
    /// <summary>
    /// Why an operand <paramref name="steps"/> evaluate into a temporary cannot be copied out;
    /// <see cref="IrSpillApplySkip.None"/> when each one can.
    /// </summary>
    /// <param name="steps">The steps of a plan.</param>
    /// <param name="root">The root of the tree of the method in the compilation.</param>
    /// <param name="semanticModel">A semantic model of that tree.</param>
    public static IrSpillApplySkip Check(ImmutableArray<IrSpillStep> steps, SyntaxNode root,
        SemanticModel semanticModel)
    {
        foreach (var step in steps)
        {
            var skip = step switch
            {
                IrSpillValue or IrSpillCheck => Check(
                    root.FindNode(step.Node.Source.Span, getInnermostNodeForTie: true),
                    step is IrSpillValue,
                    semanticModel
                ),
                IrSpillConditional conditional => CheckBranches(conditional, root, semanticModel),
                _ => IrSpillApplySkip.None
            };
            if (skip != IrSpillApplySkip.None)
            {
                return skip;
            }
        }

        return IrSpillApplySkip.None;
    }

    private static IrSpillApplySkip CheckBranches(IrSpillConditional conditional, SyntaxNode root,
        SemanticModel semanticModel)
    {
        foreach (var branch in conditional.Branches)
        {
            var skip = Check(branch, root, semanticModel);
            if (skip != IrSpillApplySkip.None)
            {
                return skip;
            }
        }

        return IrSpillApplySkip.None;
    }

    // An implicit conversion has the span of its operand, whose own type is what var gives the temporary; the
    // location of a check has the type of its element
    private static IrSpillApplySkip Check(SyntaxNode syntax, bool needsType, SemanticModel semanticModel)
    {
        if (syntax is not ExpressionSyntax expression ||
            (needsType && semanticModel.GetTypeInfo(expression).Type is null))
        {
            return IrSpillApplySkip.NoNaturalType;
        }

        return expression.Ancestors().AsStackEnumerable().Any(static node => node is CheckedExpressionSyntax)
            ? IrSpillApplySkip.OverflowContextOperand
            : IrSpillApplySkip.None;
    }
}
