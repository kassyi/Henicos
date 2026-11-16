using Kassyi.GSharp.Compiler.Analysis.Checks;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Numeric;

/// <summary>What IntegerSemantics decides a binary operator from.</summary>
/// <param name="Kind">The operator.</param>
/// <param name="Type">The type of the result (the operator type; an enum stays the enum).</param>
/// <param name="LeftType">The type of the left operand after its implicit conversion.</param>
/// <param name="IsBuiltIn">No user-defined operator and not lifted.</param>
/// <param name="HasConstantValue">The result is a constant (written as its value).</param>
/// <param name="Left">The range of the left operand; null where the decision does not read it.</param>
/// <param name="Right">The range of the right operand; null where the decision does not read it.</param>
internal readonly record struct IntegerBinaryFacts(
    BinaryOperatorKind Kind,
    ITypeSymbol? Type,
    ITypeSymbol? LeftType,
    bool IsBuiltIn,
    bool HasConstantValue,
    IntegerRange? Left,
    IntegerRange? Right)
{
    /// <summary>The facts of operation, the syntax path.</summary>
    public static IntegerBinaryFacts Of(IBinaryOperation operation) =>
        new(
            operation.OperatorKind,
            operation.Type,
            operation.LeftOperand.Type,
            operation.OperatorMethod == null && !operation.IsLifted,
            operation.ConstantValue.HasValue,
            IntegerOperationFacts.ReadsRanges(operation.OperatorKind)
                ? OperationRanges.Of(operation.LeftOperand)
                : null,
            IntegerOperationFacts.ReadsRanges(operation.OperatorKind)
                ? OperationRanges.Of(operation.RightOperand)
                : null);
}

/// <summary>What IntegerSemantics decides a unary operator from.</summary>
/// <param name="Kind">The operator.</param>
/// <param name="Type">The type of the result.</param>
/// <param name="IsBuiltIn">No user-defined operator and not lifted.</param>
/// <param name="HasConstantValue">The result is a constant.</param>
/// <param name="Operand">The range of the operand; null where the decision does not read it.</param>
internal readonly record struct IntegerUnaryFacts(
    UnaryOperatorKind Kind,
    ITypeSymbol? Type,
    bool IsBuiltIn,
    bool HasConstantValue,
    IntegerRange? Operand)
{
    /// <summary>The facts of operation, the syntax path.</summary>
    public static IntegerUnaryFacts Of(IUnaryOperation operation) =>
        new(
            operation.OperatorKind,
            operation.Type,
            operation.OperatorMethod == null && !operation.IsLifted,
            operation.ConstantValue.HasValue,
            operation.OperatorKind == UnaryOperatorKind.Minus ? OperationRanges.Of(operation.Operand) : null);
}

/// <summary>What IntegerSemantics decides an increment or decrement from.</summary>
/// <param name="IsIncrement">++ (false for --).</param>
/// <param name="Type">The type of the operation (IIncrementOrDecrementOperation.Type).</param>
/// <param name="TargetType">The type of the target.</param>
/// <param name="IsBuiltIn">No user-defined operator and not lifted.</param>
internal readonly record struct IntegerStepFacts(
    bool IsIncrement,
    ITypeSymbol? Type,
    ITypeSymbol? TargetType,
    bool IsBuiltIn)
{
    /// <summary>The facts of operation, the syntax path.</summary>
    public static IntegerStepFacts Of(IIncrementOrDecrementOperation operation) =>
        new(
            operation.Kind == OperationKind.Increment,
            operation.Type,
            operation.Target.Type,
            operation.OperatorMethod == null && !operation.IsLifted);
}

/// <summary>What IntegerSemantics decides a compound assignment from.</summary>
/// <param name="Kind">The operator.</param>
/// <param name="TargetType">The type of the target.</param>
/// <param name="ValueType">The type of the value.</param>
/// <param name="IsBuiltIn">No user-defined operator and not lifted.</param>
/// <param name="Target">The range of the target; null where the decision does not read it.</param>
/// <param name="Value">The range of the value; null where the decision does not read it.</param>
internal readonly record struct IntegerCompoundFacts(
    BinaryOperatorKind Kind,
    ITypeSymbol? TargetType,
    ITypeSymbol? ValueType,
    bool IsBuiltIn,
    IntegerRange? Target,
    IntegerRange? Value)
{
    /// <summary>The facts of operation, the syntax path.</summary>
    public static IntegerCompoundFacts Of(ICompoundAssignmentOperation operation)
    {
        var readsRanges = operation.OperatorKind is BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder;
        return new(
            operation.OperatorKind,
            operation.Target.Type,
            operation.Value.Type,
            operation.OperatorMethod == null && !operation.IsLifted,
            readsRanges ? OperationRanges.Of(operation.Target) : null,
            readsRanges ? OperationRanges.Of(operation.Value) : null);
    }
}

internal static class IntegerOperationFacts
{
    /// <summary>Whether a binary operator reads the ranges of its operands.</summary>
    public static bool ReadsRanges(BinaryOperatorKind kind) =>
        kind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply or
            BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder;
}
