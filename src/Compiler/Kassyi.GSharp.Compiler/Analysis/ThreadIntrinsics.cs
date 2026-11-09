using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The thread and block indices that GSH0035 reads: a method that reads one depends on parallel execution.
/// </summary>
internal static class ThreadIntrinsics
{
    /// <summary>Whether <paramref name="operation"/> reads <c>ThreadIndex</c> or <c>BlockIndex</c> of G#.</summary>
    public static bool IsIndex(IOperation operation) =>
        operation is IPropertyReferenceOperation
        {
            Property.ContainingType: { Name: "ThreadIndex" or "BlockIndex" } type
        } &&
        type.ContainingNamespace.IsCoreNamespace();

    /// <summary>
    /// Whether <paramref name="condition"/> compares a value that reads a thread or block index with the literal 0
    /// (<c>ThreadIndex.X == 0</c>), so that one thread (per block) takes the branch.
    /// </summary>
    public static bool IsSingleThreadCondition(IOperation condition) =>
        condition is IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } equals &&
        (IsZeroComparison(equals.LeftOperand, equals.RightOperand) ||
         IsZeroComparison(equals.RightOperand, equals.LeftOperand));

    // [WHY] A written literal 0 of any integer type, as the condition was written before (ThreadIndex.X == 0u)
    private static bool IsZeroComparison(IOperation zero, IOperation value)
    {
        if (zero.Syntax is not LiteralExpressionSyntax ||
            zero.ConstantValue is not { HasValue: true, Value: 0 or 0u or 0L or 0UL })
        {
            return false;
        }

        foreach (var operation in value.DescendantsAndSelf())
        {
            if (IsIndex(operation))
            {
                return true;
            }
        }

        return false;
    }
}
