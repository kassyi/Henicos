using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Compiler.Ir.Decisions.Invariants;

/// <summary>Checks I30 field invariants for the getter, receiver copy and setter decisions (#1561).</summary>
internal static class IrAccessorDecisionInvariants
{
    public static void CheckMember(IrPropertyReference node, IrMemberDecision d,
        ImmutableArray<string>.Builder violations)
    {
        if (d.Form == IrMemberForm.Getter &&
            (node.Receiver is null || node.Property.IsIndexer || !node.Arguments.IsEmpty))
        {
            IrDecisionInvariants.AddI30(violations, node,
                "Getter requires a receiver and a property without arguments");
        }

        if (d.CopyType is not null && d.Form != IrMemberForm.Getter)
        {
            IrDecisionInvariants.AddI30(violations, node, "CopyType requires Getter");
        }
    }

    public static void CheckCopiedReceiver(IrPropertyReference node, IrMemberDecision d,
        IrTemporaryInitializers temporaries, ImmutableArray<string>.Builder violations)
    {
        if (d.CopyType is not null &&
            !(node.Receiver is { } receiver &&
              IrReadOnlyReceivers.IsCopiedBeforeTheCall(temporaries.Resolve(receiver), node.Property.GetMethod)))
        {
            IrDecisionInvariants.AddI30(violations, node,
                "CopyType requires a receiver that C# copies before the call");
        }
    }

    public static void CheckAssignment(IrAssignment node, IrAssignmentDecision d,
        ImmutableArray<string>.Builder violations)
    {
        if (d.Accessor != IrAccessorStore.None && (d.Store is not null || node.Target is not IrPropertyReference))
        {
            IrDecisionInvariants.AddI30(violations, node, "Accessor requires a property target and no Store");
        }

        var setter = node.Target is IrPropertyReference target ? target.Property.SetMethod : null;
        if (d.Accessor == IrAccessorStore.WritesBackingField && setter is not null)
        {
            IrDecisionInvariants.AddI30(violations, node, "WritesBackingField requires a property without a setter");
        }

        if (d.Accessor == IrAccessorStore.CallsSetter && setter is null)
        {
            IrDecisionInvariants.AddI30(violations, node, "CallsSetter requires a property with a setter");
        }

        if (node.IsRef && d.Accessor != IrAccessorStore.None)
        {
            IrDecisionInvariants.AddI30(violations, node, "A ref assignment has no Accessor");
        }
    }
}
