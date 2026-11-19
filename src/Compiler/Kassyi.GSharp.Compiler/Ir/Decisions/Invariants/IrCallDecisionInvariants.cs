using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Decisions.Invariants;

/// <summary>Checks I30 field invariants for <see cref="IrCallDecision"/> (#1561).</summary>
internal static class IrCallDecisionInvariants
{
    public static void CheckCall(IrCall node, IrCallDecision d, ImmutableArray<string>.Builder violations)
    {
        if (d.Arguments.Length != node.Arguments.Length)
        {
            IrDecisionInvariants.AddI30(violations, node, "Arguments length must match call Arguments length");
        }

        if (d.Claim == IrCallClaim.OtherRule && d.Target is not null)
        {
            IrDecisionInvariants.AddI30(violations, node, "OtherRule claim requires Target == null");
        }

        if (d.Claim == IrCallClaim.BclIntrinsicMap && d.Target != CallTargetKind.BclIntrinsicMap)
        {
            IrDecisionInvariants.AddI30(violations, node, "BclIntrinsicMap claim requires Target == BclIntrinsicMap");
        }

        if (d.Claim != IrCallClaim.None && d.Target == CallTargetKind.Removed)
        {
            IrDecisionInvariants.AddI30(violations, node, "Non-None claim excludes Target == Removed");
        }

        CheckArguments(node, d, violations);
    }

    private static void CheckArguments(IrCall node, IrCallDecision d, ImmutableArray<string>.Builder violations)
    {
        var count = Math.Min(d.Arguments.Length, node.Arguments.Length);
        for (var i = 0; i < count; i++)
        {
            var arg = d.Arguments[i];
            if (arg.Check.Kind == ArgumentCheckKind.Array && arg.Check.ArraySite is null)
            {
                IrDecisionInvariants.AddI30(violations, node, $"Arguments[{i}] Array check requires ArraySite");
            }

            if (arg.Check.Kind == ArgumentCheckKind.Struct && arg.Check.StructChecks.IsEmpty)
            {
                IrDecisionInvariants.AddI30(violations, node, $"Arguments[{i}] Struct check requires StructChecks");
            }

            if (arg.ConvertedTo is not null && node.Arguments[i].Parameter.RefKind != RefKind.None)
            {
                IrDecisionInvariants.AddI30(violations, node, $"Arguments[{i}] ConvertedTo requires RefKind.None");
            }

            if (arg.ConvertedTo is not null && d.Claim == IrCallClaim.OtherRule)
            {
                IrDecisionInvariants.AddI30(violations, node, $"Arguments[{i}] ConvertedTo excludes OtherRule claim");
            }
        }
    }
}
