using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The sequence of a whole compound assignment whose target and value interfere, or of a floating-point remainder
/// whose value has another type than the target, as the assignment rule of the syntax path writes it.
/// </summary>
/// <remarks>
/// The target is bound by reference; when the value may write the variable the target belongs to (or the type of the
/// target cannot be spelled), a copy of the current value follows, which the operation reads; then the value. A writer
/// tells the copy from the value by its shape (<see cref="IrSequenceSupport.HasTargetCopy"/>). The value of a
/// floating-point remainder is declared as the type of the target, with its conversion; any other value takes the
/// value below its implicit conversions.
/// </remarks>
internal static class IrCompoundSequencing
{
    /// <summary>The plan of <paramref name="compound"/>, from its operands before they are rewritten.</summary>
    public static IrSequencePlan Plan(IrCompoundAssignment compound, IrSequenceContext context)
    {
        if (compound.Operator.Method is not null || compound.Target is IrPropertyReference)
        {
            return IrSequencePlan.None;
        }

        var compilation = context.Compilation;
        var decided = compound.Decided;
        var value = IrEffects.Of(compound.Value, compilation, IrTrapReading.Decided);
        if (!IrSequenceGate.Interferes(
                IrEffects.OfLocation(compound.Target, compilation, readsValue: true, IrTrapReading.Decided), value) &&
            !decided.ConvertsValue)
        {
            return IrSequencePlan.None;
        }

        var readsBeforeValue = value.Writes.Overlaps(IrEffects.RootOf(compound.Target, compilation)) ||
                               NeedsDecltype(compound.Target.Type);
        var valueKind = decided.FloatingRemainder is not null
            ? IrSequenceTempKind.ConvertedValue
            : IrSequenceTempKind.Value;
        return IrSequencePlan.All(IrSequenceTempKind.Reference, valueKind) with { CopiesTarget = readsBeforeValue };
    }

    /// <summary>The sequence of <paramref name="compound"/> over its rewritten target and value.</summary>
    public static IrSequence Apply(IrCompoundAssignment compound, IrSequencePlan plan, IrExpression target,
        IrExpression value)
    {
        var builder = new IrSequenceBuilder();
        var newTarget = builder.SpillReference(target);
        if (plan.CopiesTarget)
        {
            builder.SpillCopy(newTarget, compound.Target.Type);
        }

        var newValue = plan.KindOf(1) == IrSequenceTempKind.ConvertedValue
            ? builder.SpillConverted(value, compound.Target.Type)
            : builder.SpillValue(value);
        return builder.Build(compound.Update(newTarget, newValue), compound);
    }

    // A type the syntax path cannot spell, so it writes decltype of the copy of the target
    private static bool NeedsDecltype(ITypeSymbol? type) =>
        type is null or ITypeParameterSymbol or { TypeKind: TypeKind.Error };
}
