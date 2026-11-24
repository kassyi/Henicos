using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>The shapes of the sequence pass that a writer reads and the forms it cannot spell yet.</summary>
internal static class IrSequenceSupport
{
    /// <summary>
    /// Whether <paramref name="sequence"/> is a compound assignment over a reference to its target that copies the
    /// current value of the target first: its second declaration reads the first, and the operation reads the copy.
    /// </summary>
    public static bool HasTargetCopy(IrSequence sequence) =>
        sequence is
        {
            Value: IrCompoundAssignment { Target: IrVariableReference target },
            SideEffects: [IrLocalDeclaration { Variable: var reference }, IrLocalDeclaration second, ..]
        } &&
        target.Variable == reference &&
        reference.RefKind != RefKind.None &&
        second.Initializer is IrVariableReference { Category: not IrCategory.Location } copied &&
        copied.Variable == reference;

    /// <summary>
    /// Whether <paramref name="node"/> is a step or compound assignment through a helper, which a writer spells with
    /// its target twice, whose target holds a sequence (the null check of an element whose array and index interfere):
    /// the syntax path spells that sequence twice, with new numbers, which a writer cannot do yet.
    /// </summary>
    public static bool SpellsTargetTwiceWithSequence(IrExpression node) => node switch
    {
        IrCompoundAssignment compound => IrTargetPartSequencing.AppliesTo(compound) && HoldsSequence(compound.Target),
        IrIncrement increment => IrTargetPartSequencing.AppliesTo(increment) && HoldsSequence(increment.Target),
        _ => false
    };

    private static bool HoldsSequence(IrExpression target) =>
        IrNodeChildren.Exists(target, static node => node is IrSequence);
}
