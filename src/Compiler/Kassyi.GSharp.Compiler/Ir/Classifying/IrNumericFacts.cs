using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// Extracts facts for <see cref="IntegerSemantics"/> and <see cref="ArithmeticConversions"/> from the IR (#1561).
/// </summary>
internal static class IrNumericFacts
{
    /// <summary>The facts of <paramref name="binary"/>, the IR path.</summary>
    public static IntegerBinaryFacts Binary(IrBinary binary) => Binary(binary, null);

    /// <summary>
    /// The facts of <paramref name="binary"/>, resolving temporary variable references through
    /// <paramref name="temporaries"/> when provided.
    /// </summary>
    public static IntegerBinaryFacts Binary(IrBinary binary, IrTemporaryInitializers? temporaries) =>
        new(
            binary.Operator.Kind,
            binary.Type,
            binary.Left.Type,
            binary.Operator.Method == null,
            binary.ConstantValue.HasValue,
            IntegerOperationFacts.ReadsRanges(binary.Operator.Kind) ? IrRanges.Of(binary.Left, temporaries) : null,
            IntegerOperationFacts.ReadsRanges(binary.Operator.Kind) ? IrRanges.Of(binary.Right, temporaries) : null);

    /// <summary>The facts of <paramref name="unary"/>, the IR path.</summary>
    public static IntegerUnaryFacts Unary(IrUnary unary) => Unary(unary, null);

    /// <summary>
    /// The facts of <paramref name="unary"/>, resolving temporary variable references through
    /// <paramref name="temporaries"/> when provided.
    /// </summary>
    public static IntegerUnaryFacts Unary(IrUnary unary, IrTemporaryInitializers? temporaries) =>
        new(
            unary.Operator.Kind,
            unary.Type,
            unary.Operator.Method == null,
            unary.ConstantValue.HasValue,
            unary.Operator.Kind == UnaryOperatorKind.Minus ? IrRanges.Of(unary.Operand, temporaries) : null);

    /// <summary>The facts of <paramref name="increment"/>, the IR path.</summary>
    public static IntegerStepFacts Step(IrIncrement increment) => Step(increment, null);

    /// <summary>
    /// The facts of <paramref name="increment"/>, accepting optional <paramref name="temporaries"/>.
    /// </summary>
    public static IntegerStepFacts Step(IrIncrement increment, IrTemporaryInitializers? temporaries) =>
        new(
            !increment.IsDecrement,
            increment.Type,
            increment.Target.Type,
            increment.OperatorMethod == null);

    /// <summary>The facts of <paramref name="compound"/>, the IR path.</summary>
    public static IntegerCompoundFacts Compound(IrCompoundAssignment compound) => Compound(compound, null);

    /// <summary>
    /// The facts of <paramref name="compound"/>, resolving temporary variable references through
    /// <paramref name="temporaries"/> when provided.
    /// </summary>
    public static IntegerCompoundFacts Compound(IrCompoundAssignment compound, IrTemporaryInitializers? temporaries)
    {
        var readsRanges = compound.Operator.Kind is BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder;
        return new(
            compound.Operator.Kind,
            compound.Target.Type,
            compound.Value.Type,
            compound.Operator.Method == null,
            readsRanges ? IrRanges.Of(compound.Target, temporaries) : null,
            readsRanges ? IrRanges.Of(compound.Value, temporaries) : null);
    }

    /// <summary>
    /// The type of <paramref name="operand"/> before the implicit conversion the binding put on it, as
    /// <see cref="ArithmeticConversions"/> reads it from an operation.
    /// </summary>
    public static ITypeSymbol? SourceType(IrExpression operand) => SourceType(operand, null);

    /// <summary>
    /// The type of <paramref name="operand"/> before the implicit conversion the binding put on it, resolving
    /// temporary variable references through <paramref name="temporaries"/> when provided.
    /// </summary>
    public static ITypeSymbol? SourceType(IrExpression operand, IrTemporaryInitializers? temporaries)
    {
        operand = temporaries?.Resolve(operand) ?? operand;
        return operand is IrConversion { IsImplicit: true, Operand.Type: { } source } ? source : operand.Type;
    }
}
