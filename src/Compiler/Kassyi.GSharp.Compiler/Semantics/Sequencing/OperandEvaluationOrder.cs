namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// Decides which operands of a C# form interfere in C# evaluation order (C# spec §12.4.1, §12.6.2.3) and therefore
/// must be evaluated into temporaries before the C++ form (#608).
/// </summary>
internal static class OperandEvaluationOrder
{
    /// <summary>
    /// The index of the last operand that must be evaluated into a temporary, or -1 when no two operands interfere.
    /// <paramref name="operands"/> are in the C# order of evaluation.
    /// </summary>
    public static int LastSpilledOperand(ReadOnlySpan<ExpressionEffects> operands)
    {
        for (var j = operands.Length - 1; j > 0; j--)
        {
            for (var i = 0; i < j; i++)
            {
                if (operands[i].InterferesWith(operands[j]))
                {
                    return j;
                }
            }
        }

        return -1;
    }
}
