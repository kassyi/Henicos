using Kassyi.GSharp.Compiler.Ir.Expressions;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// Extracts facts for <see cref="IntegerSemantics"/> and <see cref="ArithmeticConversions"/> from the IR (#1561).
/// </summary>
internal static class IrNumericFacts
{
    /// <summary>The facts of <paramref name="binary"/>, the IR path.</summary>
    public static IntegerBinaryFacts Binary(IrBinary binary) =>
        new(
            binary.Operator.Kind,
            binary.Type,
            binary.Left.Type,
            binary.Operator.Method == null,
            binary.ConstantValue.HasValue,
            IntegerOperationFacts.ReadsRanges(binary.Operator.Kind) ? IrRanges.Of(binary.Left) : null,
            IntegerOperationFacts.ReadsRanges(binary.Operator.Kind) ? IrRanges.Of(binary.Right) : null);

    /// <summary>The facts of <paramref name="unary"/>, the IR path.</summary>
    public static IntegerUnaryFacts Unary(IrUnary unary) =>
        new(
            unary.Operator.Kind,
            unary.Type,
            unary.Operator.Method == null,
            unary.ConstantValue.HasValue,
            unary.Operator.Kind == UnaryOperatorKind.Minus ? IrRanges.Of(unary.Operand) : null);

    /// <summary>The facts of <paramref name="increment"/>, the IR path.</summary>
    public static IntegerStepFacts Step(IrIncrement increment) =>
        new(
            !increment.IsDecrement,
            increment.Type,
            increment.Target.Type,
            increment.OperatorMethod == null);

    /// <summary>The facts of <paramref name="compound"/>, the IR path.</summary>
    public static IntegerCompoundFacts Compound(IrCompoundAssignment compound)
    {
        var readsRanges = compound.Operator.Kind is BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder;
        return new(
            compound.Operator.Kind,
            compound.Target.Type,
            compound.Value.Type,
            compound.Operator.Method == null,
            readsRanges ? IrRanges.Of(compound.Target) : null,
            readsRanges ? IrRanges.Of(compound.Value) : null);
    }

    /// <summary>
    /// The type of <paramref name="operand"/> before the implicit conversion the binding put on it, as
    /// <see cref="ArithmeticConversions"/> reads it from an operation.
    /// </summary>
    public static ITypeSymbol? SourceType(IrExpression operand) =>
        operand is IrConversion { IsImplicit: true, Operand.Type: { } source } ? source : operand.Type;
}
