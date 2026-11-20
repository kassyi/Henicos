using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The sequence of the parts of a target that a writer spells twice: a step or compound assignment through a helper
/// (<c>x = helper(x, v)</c>) evaluates the location once in C#, so the parts of it whose evaluation writes something,
/// or that interfere with a later part that writes, go into temporaries first, as
/// <c>IntegerSemanticsEmitter.AppendWithSpilledTarget</c> writes them.
/// </summary>
/// <remarks>
/// <para>
/// The parts are found as the syntax path finds them: an element gives its array (recursively) and its index (a
/// value); a field gives its receiver (recursively); anything else, a field of the implicit <see langword="this"/>
/// included, is the location itself (a reference). The value of a compound assignment stays in place.
/// </para>
/// <para>
/// The elements of the target keep their own plan (<see cref="IrElementSequencing"/>), made from the parts before they
/// are replaced. A writer cannot spell such a target twice with one numbering yet
/// (<see cref="IrSequenceSupport.SpellsTargetTwiceWithSequence"/>).
/// </para>
/// </remarks>
internal static class IrTargetPartSequencing
{
    /// <summary>
    /// Whether <paramref name="node"/> is a step or compound assignment through a helper whose target is not a
    /// property with accessors.
    /// </summary>
    public static bool AppliesTo(IrExpression node) => node switch
    {
        IrCompoundAssignment compound => compound.Operator.Method is null &&
                                         compound.Target is not IrPropertyReference &&
                                         (compound.Decided.Helper ?? compound.Decided.FloatingRemainder) is not null,
        IrIncrement increment => increment.OperatorMethod is null && increment.Target is not IrPropertyReference &&
                                 increment.Decided.Helper is not null,
        _ => false
    };

    /// <summary>
    /// The plan of <paramref name="target"/>: the parts that go into temporaries, by their index in
    /// <see cref="PartsOf"/>.
    /// </summary>
    public static IrSequencePlan Plan(IrExpression target, IrSequenceContext context)
    {
        var parts = PartsOf(target);
        var effects = new ExpressionEffects[parts.Length];
        var anyWrites = false;
        for (var i = 0; i < parts.Length; i++)
        {
            effects[i] = IrEffects.Of(parts[i].Expression, context.Compilation, IrTrapReading.Decided);
            anyWrites |= !effects[i].Writes.IsEmpty;
        }

        if (!anyWrites)
        {
            return IrSequencePlan.None;
        }

        var temps = ImmutableArray.CreateBuilder<IrSequenceTempPlan>();
        for (var i = 0; i < parts.Length; i++)
        {
            if (!effects[i].Writes.IsEmpty || InterferesWithLaterWrite(effects, i))
            {
                temps.Add(new IrSequenceTempPlan(i,
                    parts[i].Reference ? IrSequenceTempKind.Reference : IrSequenceTempKind.Value));
            }
        }

        return new IrSequencePlan(temps.ToImmutable());
    }

    /// <summary>
    /// <paramref name="target"/> rebuilt with the planned parts replaced by reads of temporaries that
    /// <paramref name="builder"/> declares, in order; <paramref name="visit"/> rewrites every other part.
    /// </summary>
    public static IrExpression Apply(IrExpression target, IrSequencePlan plan, IrSequenceBuilder builder,
        Func<IrExpression, IrExpression> visit, IrSequenceContext context)
    {
        var parts = PartsOf(target);
        var replaced = new Dictionary<IrExpression, IrExpression>();
        foreach (var temp in plan.Temps)
        {
            var part = parts[temp.OperandIndex].Expression;
            var initializer = visit(part);
            replaced[part] = temp.Kind == IrSequenceTempKind.Reference
                ? builder.SpillReference(initializer)
                : builder.SpillValue(initializer);
        }

        return Rebuild(target, replaced, visit, context);
    }

    /// <summary>
    /// The parts of <paramref name="target"/> in the C# order, and whether each is bound by reference.
    /// </summary>
    public static ImmutableArray<(IrExpression Expression, bool Reference)> PartsOf(IrExpression target)
    {
        var parts = ImmutableArray.CreateBuilder<(IrExpression Expression, bool Reference)>();
        AddParts(target, parts);
        return parts.ToImmutable();
    }

    private static void AddParts(IrExpression target,
        ImmutableArray<(IrExpression Expression, bool Reference)>.Builder parts)
    {
        switch (target)
        {
            case IrElementReference element:
                AddParts(element.Receiver, parts);
                parts.Add((element.Index, false));
                return;
            case IrFieldReference { Receiver: { } receiver } when !IsImplicitThis(receiver):
                AddParts(receiver, parts);
                return;
            default:
                parts.Add((target, true));
                return;
        }
    }

    // The receiver C# inserts for a member of the containing struct; the syntax path sees the member name alone
    private static bool IsImplicitThis(IrExpression receiver) =>
        receiver is IrVariableReference { Variable.Kind: IrVariableKind.This, Source.Synthesized: true };

    private static bool InterferesWithLaterWrite(ExpressionEffects[] effects, int index)
    {
        for (var j = index + 1; j < effects.Length; j++)
        {
            if (!effects[j].Writes.IsEmpty && effects[index].InterferesWith(effects[j]))
            {
                return true;
            }
        }

        return false;
    }

    // The target with the replaced parts; the elements on the way keep their own plan
    private static IrExpression Rebuild(IrExpression node, Dictionary<IrExpression, IrExpression> replaced,
        Func<IrExpression, IrExpression> visit, IrSequenceContext context)
    {
        if (replaced.TryGetValue(node, out var replacement))
        {
            return replacement;
        }

        switch (node)
        {
            case IrElementReference element:
            {
                var plan = IrElementSequencing.Plan(element, context);
                var receiver = Rebuild(element.Receiver, replaced, visit, context);
                var index = Rebuild(element.Index, replaced, visit, context);
                return plan.IsSequenced
                    ? IrElementSequencing.Apply(element, receiver, index)
                    : element.Update(receiver, index);
            }
            case IrFieldReference { Receiver: { } receiver } field:
                return field.Update(Rebuild(receiver, replaced, visit, context));
            default:
                return visit(node);
        }
    }
}
