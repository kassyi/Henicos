using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The sequence of a simple assignment whose target and value interfere (C++17 evaluates the right side first), as
/// the assignment rule of the syntax path writes it.
/// </summary>
/// <remarks>
/// <para>
/// An element of an array is not bound before the value runs, since C# checks it when it stores: the array (by
/// reference for a stack array), the index and the value go into temporaries, and the element of the target is not
/// sequenced on its own. Any other target is bound by reference, then the value.
/// </para>
/// <para>
/// The value is declared as the type of the target, with its conversion. A ref assignment, a discard and a property
/// with accessors are written by other rules.
/// </para>
/// </remarks>
internal static class IrAssignmentSequencing
{
    /// <summary>The plan of <paramref name="assignment"/>, from its operands before they are rewritten.</summary>
    public static IrSequencePlan Plan(IrAssignment assignment, IrSequenceContext context)
    {
        if (assignment.IsRef || assignment.Target is IrPropertyReference or IrDiscard)
        {
            return IrSequencePlan.None;
        }

        var compilation = context.Compilation;
        if (!IrSequenceGate.Interferes(
                IrEffects.OfAssignmentTarget(assignment.Target, compilation, IrTrapReading.Decided),
                IrEffects.Of(assignment.Value, compilation, IrTrapReading.Decided)))
        {
            return IrSequencePlan.None;
        }

        if (IsElementStore(assignment))
        {
            var array = assignment.Decided.Store is { BindsArrayByReference: true }
                ? IrSequenceTempKind.Reference
                : IrSequenceTempKind.Value;
            return IrSequencePlan.All(array, IrSequenceTempKind.Value, IrSequenceTempKind.ConvertedValue);
        }

        return IrSequencePlan.All(IrSequenceTempKind.Reference, IrSequenceTempKind.ConvertedValue);
    }

    /// <summary>
    /// The sequence of <paramref name="assignment"/>; <paramref name="visit"/> rewrites each operand, the array and the
    /// index of an element target one by one.
    /// </summary>
    public static IrSequence Apply(IrAssignment assignment, IrSequencePlan plan,
        Func<IrExpression, IrExpression> visit)
    {
        var builder = new IrSequenceBuilder();
        var targetType = assignment.Target.Type;
        if (IsElementStore(assignment) && assignment.Target is IrElementReference element)
        {
            var receiver = visit(element.Receiver);
            var index = visit(element.Index);
            var value = visit(assignment.Value);
            var newReceiver = plan.KindOf(0) == IrSequenceTempKind.Reference
                ? builder.SpillReference(receiver)
                : builder.SpillValue(receiver);
            var newIndex = builder.SpillValue(index);
            var newValue = builder.SpillConverted(value, targetType);
            return builder.Build(assignment.Update(element.Update(newReceiver, newIndex), newValue), assignment);
        }

        var target = visit(assignment.Target);
        var assigned = visit(assignment.Value);
        var newTarget = builder.SpillReference(target);
        var newAssigned = builder.SpillConverted(assigned, targetType);
        return builder.Build(assignment.Update(newTarget, newAssigned), assignment);
    }

    // A store into an element of a one-dimensional array, which the decision pass planned
    private static bool IsElementStore(IrAssignment assignment) =>
        assignment.Target is IrElementReference && assignment.Decided.Store is not null;
}
