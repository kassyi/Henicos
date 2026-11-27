namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// Whether operands interfere, by the rule the syntax path uses (<see cref="OperandEvaluationOrder.LastSpilledOperand"/>,
/// with <see cref="ExpressionEffects.InterferesWith"/>); the IR has no copy of the rule.
/// </summary>
internal static class IrSequenceGate
{
    /// <summary>Whether <paramref name="first"/> and the later <paramref name="second"/> interfere.</summary>
    public static bool Interferes(ExpressionEffects first, ExpressionEffects second) =>
        OperandEvaluationOrder.LastSpilledOperand([first, second]) >= 0;
}
