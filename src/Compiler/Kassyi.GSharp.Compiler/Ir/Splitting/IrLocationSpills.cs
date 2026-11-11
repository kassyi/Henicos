using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The parts of a location that <see cref="IrSpillPlanner"/> evaluates before a split: a location is not copied, so
/// what selects it is (Roslyn's <c>SpillSequenceSpiller</c> spills the same parts of an assignment target).
/// </summary>
/// <remarks>
/// Not planned yet: Roslyn also evaluates a location that is only the receiver of a field or a call
/// (<c>a[i].F = await x</c>, <c>a[i].M(await x)</c>) once for its exceptions before the split, so that an index out of
/// range or a null array throws before it. The plan spills the array and the index, and the access itself, with its
/// checks, runs after the split. The step that puts that check before the split belongs to the stage that writes the
/// plan out, where the checks are emitted.
/// </remarks>
internal static class IrLocationSpills
{
    /// <summary>
    /// Plans the parts of <paramref name="location"/>, evaluated before a split: the array (unless it is a stack array)
    /// and the index of an element, the receiver of a field, the receiver and the indices of a span element; a variable
    /// has none. Any other location falls back (<see cref="IrSpillFallback.LocationNotRebuildable"/>).
    /// </summary>
    public static void PlanParts(IrSpillPlanner planner, IrExpression location, in IrSpillContext context,
        ImmutableArray<IrSpillStep>.Builder steps)
    {
        switch (location)
        {
            case IrVariableReference or IrDiscard or IrFieldReference { Receiver: null }:
                return;
            case IrElementReference element:
                if (!IrStayInPlace.IsStackArray(element.Receiver))
                {
                    planner.PlanValueBefore(element.Receiver, context, steps);
                }

                PlanIndex(planner, element.Index, context, steps);
                return;
            case IrFieldReference { Receiver: { } receiver }:
                planner.PlanBefore(receiver, context, steps);
                return;
            case IrPropertyReference { Property.IsIndexer: true, Receiver: { Type.IsRefLikeType: true } receiver }
                property:
                planner.PlanBefore(receiver, context, steps);
                foreach (var index in IrOperandLists.Of(null, property.Arguments))
                {
                    PlanIndex(planner, index, context, steps);
                }

                return;
            default:
                planner.Fail(IrSpillFallback.LocationNotRebuildable);
                return;
        }
    }

    /// <summary>A location whose parts hold a split: its parts are operands like any other.</summary>
    public static void PlanHoldingSplit(IrSpillPlanner planner, IrExpression location,
        ImmutableArray<IrSpillStep>.Builder steps)
    {
        switch (location)
        {
            case IrElementReference element:
                planner.PlanOperands([element.Receiver, element.Index], IrSpillRole.TargetPart, steps);
                return;
            case IrFieldReference { Receiver: { } receiver }:
                planner.PlanOperands([receiver], IrSpillRole.TargetPart, steps);
                return;
            case IrPropertyReference { Property.IsIndexer: true, Receiver.Type.IsRefLikeType: true } property:
                planner.PlanOperands(IrOperandLists.Of(property.Receiver, property.Arguments), IrSpillRole.TargetPart,
                    steps);
                return;
            default:
                planner.Fail(IrSpillFallback.LocationNotRebuildable);
                return;
        }
    }

    // [WHY] The index of a target that is not an argument of a call that splits is read once, before the split, and
    // never again: one that cannot throw and reads only what stays in place (IrStayInPlace.StaysAsIndex) gives the
    // same value after the split. The arguments of a call that splits keep the narrower rule of the syntax passes
    // until the plan replaces them.
    private static void PlanIndex(IrSpillPlanner planner, IrExpression index, in IrSpillContext context,
        ImmutableArray<IrSpillStep>.Builder steps)
    {
        if (context.Role == IrSpillRole.TargetPart && !planner.Holds(index) &&
            IrStayInPlace.StaysAsIndex(index, context.Others, context.Later, planner.Compilation))
        {
            return;
        }

        planner.PlanValueBefore(index, context, steps);
    }
}
