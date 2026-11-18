using Kassyi.GSharp.Compiler.Ir.Linear;

namespace Kassyi.GSharp.Compiler.Ir.Frames;

/// <summary>
/// Checks the invariants of <see cref="IrLiveness"/> produced by <see cref="IrLivenessAnalysis"/>: variable numbering
/// and non-decreasing declaration position, <c>L_source ⊆ L_all</c>, <c>L_all ⊆ T</c>, and empty sets when
/// <see cref="IrLiveness.HasSplit"/> is <see langword="false"/>.
/// </summary>
internal static class IrLivenessInvariants
{
    /// <summary>
    /// The invariant violations in <paramref name="liveness"/> for <paramref name="machine"/>; empty when valid.
    /// </summary>
    public static ImmutableArray<string> Verify(IrStateMachine machine, IrLiveness liveness)
    {
        var violations = ImmutableArray.CreateBuilder<string>();
        var variables = liveness.Variables;
        for (var i = 0; i < variables.Length; i++)
        {
            var variable = variables[i];
            CheckVariableOrder(variable, i, i > 0 ? variables[i - 1] : null, violations);
            CheckVariableSets(liveness, variable, violations);
        }

        if (machine.States.IsEmpty && !variables.IsEmpty)
        {
            violations.Add("LV1: an empty state machine cannot declare variables");
        }

        return violations.ToImmutable();
    }

    private static void CheckVariableOrder(IrLivenessVariable variable, int expectedIndex,
        IrLivenessVariable? previous, ImmutableArray<string>.Builder violations)
    {
        if (variable.Index != expectedIndex)
        {
            violations.Add(
                $"LV1 at {variable.Declaration.State}:{variable.Declaration.Index}: " +
                $"variable '{variable.Variable.Name}' has index {variable.Index}, expected {expectedIndex}");
        }

        if (previous is not null && variable.Declaration.CompareTo(previous.Declaration) < 0)
        {
            violations.Add(
                $"LV1 at {variable.Declaration.State}:{variable.Declaration.Index}: " +
                $"variable '{variable.Variable.Name}' precedes '{previous.Variable.Name}' " +
                $"({previous.Declaration.State}:{previous.Declaration.Index})");
        }
    }

    private static void CheckVariableSets(IrLiveness liveness, IrLivenessVariable variable,
        ImmutableArray<string>.Builder violations)
    {
        var pos = $"{variable.Declaration.State}:{variable.Declaration.Index}";
        if (liveness.IsLiveAcrossSourceSplit(variable) && !liveness.IsLiveAcrossAnySplit(variable))
        {
            violations.Add(
                $"LV2 at {pos}: variable '{variable.Variable.Name}' is in L_source without being in L_all");
        }

        if (liveness.IsLiveAcrossAnySplit(variable) && !liveness.IsTouchedInAnotherState(variable))
        {
            violations.Add(
                $"LV3 at {pos}: variable '{variable.Variable.Name}' is in L_all without being in T");
        }

        if (!liveness.HasSplit &&
            (liveness.IsLiveAcrossSourceSplit(variable) ||
             liveness.IsLiveAcrossAnySplit(variable) ||
             liveness.IsTouchedInAnotherState(variable) ||
             liveness.IsPinned(variable)))
        {
            violations.Add(
                $"LV4 at {pos}: variable '{variable.Variable.Name}' has non-empty liveness sets without a split");
        }
    }
}
