using System.Numerics;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Whether evaluating an expression may raise a run-time error that C# defines (an exception): an integer division or
/// remainder by a divisor that may be zero or -1, checked arithmetic or a checked conversion, an element access, a call
/// (other than floating-point math), an object creation, a computed property, a user-defined operator or conversion, or
/// a throw. Any operation this class does not list may trap.
/// </summary>
/// <remarks>
/// [WHY] A rewrite that skips the evaluation of an expression (a fused StackLinq Skip that narrows the range instead of
/// calling the selector for the elements it drops) is valid only when the expression neither writes
/// (<see cref="ExpressionEffects"/>) nor traps: C# raises the error, so the native, managed and checked CUDA builds must
/// raise it too.
/// </remarks>
internal static class ExpressionTraps
{
    /// <summary>Whether evaluating <paramref name="expression"/> may raise a run-time error.</summary>
    public static bool MayTrap(ExpressionSyntax expression, SemanticModel model) =>
        model.GetSafeOperation(expression) is not { } operation || MayTrap(operation);

    private static bool MayTrap(IOperation operation)
    {
        if (!IsTrapFree(operation))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (MayTrap(child))
            {
                return true;
            }
        }

        return false;
    }

    // The operation itself, not its operands
    private static bool IsTrapFree(IOperation operation) => operation switch
    {
        ILiteralOperation or ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation or
            IDefaultValueOperation or IParenthesizedOperation or IConditionalOperation or IArgumentOperation => true,
        IPropertyReferenceOperation { Property: var property } => !property.IsIndexer && IsStoredOrIntrinsic(property),
        IBinaryOperation binary => binary is { OperatorMethod: null, IsChecked: false } && !MayDivideByZeroOrOverflow(binary),
        IUnaryOperation unary => unary is { OperatorMethod: null, IsChecked: false },
        IConversionOperation conversion => conversion is { OperatorMethod: null, IsChecked: false },
        IInvocationOperation invocation => IsFloatingPointMath(invocation.TargetMethod),
        _ => false
    };

    // A field-like property, or the length of an array or span: a computed property of a struct runs its getter
    private static bool IsStoredOrIntrinsic(IPropertySymbol property) =>
        !StructAbiMembers.IsComputedProperty(property) ||
        property.ContainingType.SpecialType == SpecialType.System_Array ||
        (property.GetMethod is { } getter && DeviceFunctionEffects.IsPureIntrinsic(getter));

    // x / y and x % y of an integer type: y == 0 raises DivideByZeroException, MinValue / -1 OverflowException
    private static bool MayDivideByZeroOrOverflow(IBinaryOperation binary)
    {
        if (binary.OperatorKind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder) ||
            binary.Type?.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr or
                SpecialType.System_UIntPtr))
        {
            return false;
        }

        return binary.RightOperand.ConstantValue is not { HasValue: true, Value: { } value } ||
               !IntegerSemantics.TryGetInteger(value, out var divisor) ||
               divisor.IsZero ||
               divisor == BigInteger.MinusOne;
    }

    // Math.Sqrt, MathF.Max, float.IsNaN ...: pure, and over floating-point values only, which raise nothing
    private static bool IsFloatingPointMath(IMethodSymbol method) =>
        DeviceFunctionEffects.IsPureIntrinsic(method) &&
        (IsFloatingPoint(method.ReturnType) || method.ReturnType.SpecialType == SpecialType.System_Boolean) &&
        method.Parameters
            .AsStackEnumerable()
            .All(static p => p.RefKind == RefKind.None && IsFloatingPoint(p.Type));

    private static bool IsFloatingPoint(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
}
