using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Decisions.Invariants;

/// <summary>
/// Checks I30 field invariants for null-check, store, null-test, coalesce, and member decisions (#1561).
/// </summary>
internal static class IrNullDecisionInvariants
{
    public static void CheckElement(IrElementReference node, IrElementDecision d,
        ImmutableArray<string>.Builder violations)
    {
        if ((d.Check == IrElementCheck.NullChecked) != (d.Site is not null))
        {
            IrDecisionInvariants.AddI30(violations, node, "NullChecked iff Site is non-null");
        }

        if (d.Site is { TrapKind: not ArrayNullCheck.TrapKind })
        {
            IrDecisionInvariants.AddI30(violations, node, $"Site TrapKind must be {ArrayNullCheck.TrapKind}");
        }
    }

    public static void CheckAssignment(IrAssignment node, IrAssignmentDecision d,
        ImmutableArray<string>.Builder violations)
    {
        if (d.Store is { Site: not null, BindsArrayByReference: true })
        {
            IrDecisionInvariants.AddI30(violations, node, "Store with Site cannot bind by reference");
        }
    }

    public static void CheckNullTest(IrNullTest node, IrNullTestDecision d,
        ImmutableArray<string>.Builder violations)
    {
        if (d.OperandIsStackArrayView && d.Form != NullForm.ArrayView)
        {
            IrDecisionInvariants.AddI30(violations, node, "OperandIsStackArrayView requires ArrayView");
        }

        if (d.SkippedConversions < 0)
        {
            IrDecisionInvariants.AddI30(violations, node, "SkippedConversions cannot be negative");
        }
    }

    public static void CheckCoalesce(IrCoalesce node, IrCoalesceDecision d,
        ImmutableArray<string>.Builder violations)
    {
        if (d.LeftIsStackArrayView && d.LeftForm != NullForm.ArrayView)
        {
            IrDecisionInvariants.AddI30(violations, node, "LeftIsStackArrayView requires ArrayView");
        }

        if (d.LeftForm == NullForm.ArrayView && d.CastsToResult)
        {
            IrDecisionInvariants.AddI30(violations, node, "ArrayView excludes CastsToResult");
        }
    }

    public static void CheckMember(IrPropertyReference node, IrMemberDecision d,
        ImmutableArray<string>.Builder violations)
    {
        if ((d.Form == IrMemberForm.StackArrayLength) != (d.StackArrayLength >= 0))
        {
            IrDecisionInvariants.AddI30(violations, node, "StackArrayLength iff StackArrayLength >= 0");
        }

        if (d.Form != IrMemberForm.StackArrayLength && d.StackArrayLength != -1)
        {
            IrDecisionInvariants.AddI30(violations, node, "Non-StackArrayLength requires StackArrayLength == -1");
        }

        if (d.ReceiverSite is not null && d.Form != IrMemberForm.ArrayMember)
        {
            IrDecisionInvariants.AddI30(violations, node, "ReceiverSite requires ArrayMember");
        }
    }
}
