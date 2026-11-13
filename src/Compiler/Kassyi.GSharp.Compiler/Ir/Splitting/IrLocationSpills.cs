using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The parts of a location that <see cref="IrSpillPlanner"/> evaluates before a split: a location is not copied, so
/// what selects it is (Roslyn's <c>SpillSequenceSpiller</c> spills the same parts of an assignment target).
/// </summary>
/// <remarks>
/// An element C# selects before the split is also evaluated once for its checks there (<see cref="IrSpillCheck"/>), so
/// that an index out of range or a null array throws before the split, as Roslyn evaluates the receiver of
/// <c>a[i].F = await x</c> and <c>a[i].M(await x)</c> once for its exceptions. Only an array element that is the target
/// of a simple assignment, which C# checks when it stores the value, and the target of a compound assignment, which
/// its read checks, get none (<see cref="IrLocationCheck"/>).
/// </remarks>
internal static class IrLocationSpills
{
    /// <summary>
    /// Plans the parts of <paramref name="location"/>, evaluated before a split: the array (unless it is a stack array)
    /// and the index of an element, the receiver of a field, the receiver and the indices of a span element; a variable
    /// has none. An element is then checked where <paramref name="check"/> says. Any other location falls back
    /// (<see cref="IrSpillFallback.LocationNotRebuildable"/>).
    /// </summary>
    public static void PlanParts(IrSpillPlanner planner, IrExpression location, in IrSpillContext context,
        ImmutableArray<IrSpillStep>.Builder steps, IrLocationCheck check = IrLocationCheck.WhereSelected)
    {
        // A receiver is selected where its member is, whatever the member's own check
        var receiverCheck = check == IrLocationCheck.ByRead ? IrLocationCheck.ByRead : IrLocationCheck.WhereSelected;
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
                AddCheck(element, check == IrLocationCheck.WhereSelected, steps);
                return;
            case IrFieldReference { Receiver: { } receiver }:
                if (IsStructValue(receiver))
                {
                    planner.Fail(IrSpillFallback.StructValueReceiverTarget);
                    return;
                }

                planner.PlanBefore(receiver, context, steps, receiverCheck);
                return;
            case IrPropertyReference { Property.IsIndexer: true, Receiver: { Type.IsRefLikeType: true } receiver }
                property:
                planner.PlanBefore(receiver, context, steps, receiverCheck);
                foreach (var index in IrOperandLists.Of(null, property.Arguments))
                {
                    PlanIndex(planner, index, context, steps);
                }

                AddCheck(property, check != IrLocationCheck.ByRead, steps);
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
            case IrFieldReference { Receiver: { } receiver } when IsStructValue(receiver):
                planner.Fail(IrSpillFallback.StructValueReceiverTarget);
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

    // The element is checked before the split, after its parts, unless an operation after the split checks it
    private static void AddCheck(IrExpression element, bool beforeSplit, ImmutableArray<IrSpillStep>.Builder steps)
    {
        if (beforeSplit)
        {
            steps.Add(new IrSpillCheck(element));
        }
    }

    // [WHY] A field of a struct value is not a variable in C#: the IR never makes it a location (the builder's
    // invariants), and spilling its receiver would write a copy, so such a node is not planned
    private static bool IsStructValue(IrExpression receiver) =>
        receiver is { Type.IsValueType: true, Category: IrCategory.Value };

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
