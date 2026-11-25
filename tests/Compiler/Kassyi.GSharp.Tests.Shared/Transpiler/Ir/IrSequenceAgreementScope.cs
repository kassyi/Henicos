#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Sequencing;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Which methods the sequences of the IR are compared on with the temporaries of the syntax path: the forms the
/// sequence pass covers, without the calls and creations whose arguments the pass leaves in place and without the forms
/// other rules write. Each exclusion has a reason, counted by the comparison.
/// </summary>
internal static class IrSequenceAgreementScope
{
    /// <summary>
    /// Why <paramref name="decided"/> (sequenced as <paramref name="sequenced"/>) is not compared, or
    /// <see langword="null"/> when it is.
    /// </summary>
    public static string? ExclusionOf(IrMethod decided, IrMethod sequenced)
    {
        if (IrInvariants.ContainsOpaque(decided.Body))
        {
            return "opaque";
        }

        foreach (var node in IrNodeChildren.DescendantsAndSelf(decided.Body))
        {
            if (ReasonOf(node) is { } reason)
            {
                return reason;
            }
        }

        return IrNodeChildren.DescendantsAndSelf(sequenced.Body)
            .OfType<IrExpression>()
            .Any(IrSequenceSupport.SpellsTargetTwiceWithSequence)
            ? "target spelled twice with a sequence"
            : null;
    }

    private static string? ReasonOf(IrNode node) => node switch
    {
        IrBinary { Operator.Method: not null } or IrUnary { Operator.Method: not null } or
            IrIncrement { OperatorMethod: not null } or IrCompoundAssignment { Operator.Method: not null } or
            IrConversion { Conversion.Method: not null } => "user-defined operator",
        IrAssignment { Target: IrPropertyReference } or IrCompoundAssignment { Target: IrPropertyReference } or
            IrIncrement { Target: IrPropertyReference } => "property target",
        IrPropertyReference { Decided.Form: IrMemberForm.Other } => "other member form",
        IrExpression expression when IrSequenceSupport.SkipsArgumentsOf(expression) => "params arguments",
        IrCall call when call.Decided.Target is not (CallTargetKind.DeviceFunction or
            CallTargetKind.DialectIntrinsic or CallTargetKind.BclIntrinsicMap) => "call written by another rule",
        // The syntax walk writes the variable of a by-value receiver of a reduced extension call (IrEffectDifferences)
        IrCall
        {
            Method.Parameters: [{ RefKind: RefKind.None }, ..],
            Arguments: [{ Kind: IrArgumentKind.ExtensionReceiver }, ..]
        } =>
            IrClassificationEquivalence.KnownPrefix + "a by-value extension receiver is not written",
        IrConversion { Decided.Form: IrConversionForm.Unsupported } => "unsupported conversion",
        IrExpression expression when TargetPartsDiffer(expression) => "target part the syntax path finds otherwise",
        _ => null
    };

    // A static field or a property on the way of a target whose parts a helper spells twice: the syntax path takes
    // the parts from the spelling (a type name, a member name), the IR from the meaning
    private static bool TargetPartsDiffer(IrExpression expression)
    {
        if (!IrTargetPartSequencing.AppliesTo(expression))
        {
            return false;
        }

        var target = expression switch
        {
            IrCompoundAssignment compound => compound.Target,
            IrIncrement increment => increment.Target,
            _ => null
        };
        return target is not null && IrTargetPartSequencing.PartsOf(target)
            .Any(static part => part.Expression is IrFieldReference { Receiver: null } or IrPropertyReference);
    }
}
