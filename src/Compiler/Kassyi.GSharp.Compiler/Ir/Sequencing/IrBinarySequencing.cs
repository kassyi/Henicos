using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The sequence of a binary operator whose operands interfere, as the binary rule of the syntax path writes it: both
/// operands into temporaries, the left one first.
/// </summary>
/// <remarks>
/// A floating-point remainder (<c>fmod</c>) declares its temporaries as the type of the operation, with the implicit
/// conversions; an integer helper and an operator that C++ leaves unsequenced take the value below the implicit
/// conversions in a deduced temporary, and the conversions stay at the use. A shift without a helper keeps the order
/// in C++17, a folded constant has no operands to order, and a user-defined operator is written by another rule.
/// </remarks>
internal static class IrBinarySequencing
{
    /// <summary>The plan of <paramref name="binary"/>, from its operands before they are rewritten.</summary>
    public static IrSequencePlan Plan(IrBinary binary, IrSequenceContext context)
    {
        if (binary.Operator.Method is not null)
        {
            return IrSequencePlan.None;
        }

        var decided = binary.Decided;
        if (decided.Folded is not null)
        {
            return IrSequencePlan.None;
        }

        IrSequenceTempKind kind;
        if (decided.FloatingRemainder is not null)
        {
            kind = IrSequenceTempKind.ConvertedValue;
        }
        else if (decided.Helper is not null || !IsShift(binary.Operator.Kind))
        {
            kind = IrSequenceTempKind.Value;
        }
        else
        {
            return IrSequencePlan.None;
        }

        var compilation = context.Compilation;
        return IrSequenceGate.Interferes(
            IrEffects.Of(binary.Left, compilation, IrTrapReading.Decided),
            IrEffects.Of(binary.Right, compilation, IrTrapReading.Decided))
            ? IrSequencePlan.All(kind, kind)
            : IrSequencePlan.None;
    }

    /// <summary>The sequence of <paramref name="binary"/> over its rewritten operands.</summary>
    public static IrSequence Apply(IrBinary binary, IrSequencePlan plan, IrExpression left, IrExpression right)
    {
        var builder = new IrSequenceBuilder();
        var type = binary.Type;
        var newLeft = Spill(builder, plan.KindOf(0), left, type);
        var newRight = Spill(builder, plan.KindOf(1), right, type);
        return builder.Build(binary.Update(newLeft, newRight), binary);
    }

    private static IrExpression Spill(IrSequenceBuilder builder, IrSequenceTempKind kind, IrExpression operand,
        ITypeSymbol type) =>
        kind == IrSequenceTempKind.ConvertedValue
            ? builder.SpillConverted(operand, type)
            : builder.SpillValue(operand);

    private static bool IsShift(BinaryOperatorKind kind) =>
        kind is BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift or
            BinaryOperatorKind.UnsignedRightShift;
}
