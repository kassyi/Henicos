using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The sequence of an array element read through its null check (<c>__gsharp_array_at</c>, a call that leaves the order
/// of its arguments unspecified) when the array and the index interfere: both into deduced temporaries, the array
/// first, as <see cref="ArrayNullCheck.TryAppendCheckedElement"/> writes it.
/// </summary>
/// <remarks>
/// The initializer of the index is the index as written; a writer wraps it in the overflow check of a
/// <see langword="ulong"/> index when <see cref="IrElementDecision.IndexNeedsOverflowCheck"/> says so. An element
/// without a check is written <c>E1[E2]</c>, which C++17 sequences.
/// </remarks>
internal static class IrElementSequencing
{
    /// <summary>The plan of <paramref name="element"/>, from its operands before they are rewritten.</summary>
    public static IrSequencePlan Plan(IrElementReference element, IrSequenceContext context)
    {
        var decided = element.Decided;
        if (decided.Check != IrElementCheck.NullChecked)
        {
            return IrSequencePlan.None;
        }

        var compilation = context.Compilation;
        return IrSequenceGate.Interferes(
            IrEffects.Of(element.Receiver, compilation, IrTrapReading.Decided),
            IrEffects.Of(element.Index, compilation, IrTrapReading.Decided)
                .WithThrow(decided.IndexNeedsOverflowCheck))
            ? IrSequencePlan.All(IrSequenceTempKind.Value, IrSequenceTempKind.Value)
            : IrSequencePlan.None;
    }

    /// <summary>The sequence of <paramref name="element"/> over its rewritten array and index.</summary>
    public static IrSequence Apply(IrElementReference element, IrExpression receiver, IrExpression index)
    {
        var builder = new IrSequenceBuilder();
        var newReceiver = builder.SpillValue(receiver);
        var newIndex = builder.SpillValue(index);
        return builder.Build(element.Update(newReceiver, newIndex), element);
    }
}
