using System.Numerics;
using Kassyi.GSharp.Compiler.Analysis.Checks;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Whether evaluating an expression may raise a run-time error that C# defines (an exception): an integer division or
/// remainder by a divisor that may be zero or -1, checked arithmetic or a checked conversion, the value of a
/// <c>Nullable&lt;T&gt;</c> converted to <c>T</c>, a member of an array that may be null (<c>Length</c>), an element
/// access, a call (other than floating-point math), an object creation, a computed property, a user-defined operator or
/// conversion, or a throw. Any operation this class does not list may trap.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] A rewrite that skips the evaluation of an expression (a fused StackLinq Skip that narrows the range instead of
/// calling the selector for the elements it drops) is valid only when the expression neither writes
/// (<see cref="ExpressionEffects"/>) nor traps: C# raises the error, so the native, managed and checked CUDA builds
/// must raise it too.
/// </para>
/// <para>
/// [WHY] The C# order of evaluation decides whether a write happens before an exception (#1786 (a throwing check in a
/// later operand runs before an earlier call on MSVC)): <see cref="ExpressionEffects.MayThrow"/> is this answer, and
/// <see cref="ExpressionEffects.ThrowInterferesWith"/> orders a trap against a write to memory.
/// </para>
/// </remarks>
internal static class ExpressionTraps
{
    /// <summary>Whether evaluating <paramref name="expression"/> may raise a run-time error.</summary>
    public static bool MayTrap(ExpressionSyntax expression, SemanticModel model)
    {
        if (HasLoweredArrayCheck(expression))
        {
            return true;
        }

        return GetOperation(expression, model) is { } operation
            ? MayTrap(operation)
            : MayTrapUnresolved(expression, model);
    }

    /// <summary>
    /// Whether evaluating <paramref name="target"/>, the target of a simple assignment, to its location may raise a
    /// run-time error before the right operand is evaluated: the traps of its receiver and index, without the null and
    /// bounds check of an array element, which C# makes when it stores the value (C# spec §12.21.2: the array instance
    /// and the index are evaluated, then the right operand, then the element is checked). The CLR does the same for an
    /// element of every type, a struct included (<c>ThrowOrderDifferentialTests</c>).
    /// </summary>
    public static bool MayTrapBeforeStore(ExpressionSyntax target, SemanticModel model)
    {
        if (HasLoweredArrayCheck(target))
        {
            return true;
        }

        if (GetOperation(target, model) is not { } operation)
        {
            return MayTrapUnresolved(target, model);
        }

        if (operation is not IArrayElementReferenceOperation element)
        {
            return MayTrap(operation);
        }

        if (MayTrap(element.ArrayReference))
        {
            return true;
        }

        foreach (var index in element.Indices)
        {
            if (MayTrap(index))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MayTrap(IOperation operation)
    {
        // A lambda is not evaluated where it is written: creating it raises nothing, its body runs when it is called
        if (operation is IAnonymousFunctionOperation or
            IDelegateCreationOperation { Target: IAnonymousFunctionOperation })
        {
            return false;
        }

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

    // The operation of expression, or null for a node that lowering synthesized: a node without an operation, or one
    // whose original-span annotation leads to a node of another kind (the frame variable __gsharp_ctx->x of the
    // declarator x = <initializer>)
    private static IOperation? GetOperation(ExpressionSyntax expression, SemanticModel model) =>
        model.GetSafeOperation(expression) is { } operation && operation.Syntax.RawKind == expression.RawKind
            ? operation
            : null;

    // A node that lowering synthesized, which has no operation: a temporary or a variable of the frame
    // (__gsharp_ctx->x) reads a variable of the thread, which raises nothing; any other node may trap
    private static bool MayTrapUnresolved(ExpressionSyntax expression, SemanticModel model) => expression switch
    {
        IdentifierNameSyntax or LiteralExpressionSyntax => false,
        ParenthesizedExpressionSyntax parenthesized => MayTrap(parenthesized.Expression, model),
        MemberAccessExpressionSyntax access when MemberEffects.IsFrameAccess(access) => false,
        _ => true
    };

    // The source of a lowered foreach, which the transpiler reads through the null check of an array
    // (ArrayNullCheck.TryAppendAnnotatedCheck), while its original node is the array alone
    private static bool HasLoweredArrayCheck(ExpressionSyntax expression) =>
        expression.ContainsAnnotations &&
        expression
            .GetAnnotatedNodes(LoweringAnnotations.ArrayNullCheckAnnotationKind)
            .AsStackEnumerable()
            .Any();

    // The operation itself, not its operands
    private static bool IsTrapFree(IOperation operation) => operation switch
    {
        ILiteralOperation or ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation or
            IDefaultValueOperation or IParenthesizedOperation or IConditionalOperation or IArgumentOperation or
            IInstanceReferenceOperation or ISimpleAssignmentOperation or IDeclarationExpressionOperation or
            IDiscardOperation => true,
        IPropertyReferenceOperation { Property: var property } reference =>
            !property.IsIndexer && IsStoredOrIntrinsic(property) && !ReadsArrayThatMayBeNull(reference),
        IBinaryOperation binary =>
            binary is { OperatorMethod: null, IsChecked: false } &&
            !MayFailDivision(binary.OperatorKind, binary.Type, binary.LeftOperand, binary.RightOperand),
        ICompoundAssignmentOperation compound =>
            compound is
            {
                OperatorMethod: null, IsChecked: false, InConversion.IsUserDefined: false,
                OutConversion.IsUserDefined: false
            } &&
            !MayFailCompoundDivision(compound),
        IIncrementOrDecrementOperation step => step is { OperatorMethod: null, IsChecked: false },
        IUnaryOperation unary => unary is { OperatorMethod: null, IsChecked: false },
        IConversionOperation conversion =>
            conversion is { OperatorMethod: null, IsChecked: false } && !UnwrapsNullable(conversion),
        IInvocationOperation invocation => IsFloatingPointMath(invocation.TargetMethod),
        _ => false
    };

    // A field-like property, or the length of an array or span: a computed property of a struct runs its getter
    private static bool IsStoredOrIntrinsic(IPropertySymbol property) =>
        !StructAbiMembers.HasAccessorFunctions(property) ||
        property.ContainingType.SpecialType == SpecialType.System_Array ||
        (property.GetMethod is { } getter && DeviceIntrinsicEffects.IsPureIntrinsic(getter));

    // a.Length of an array that may be null: C# raises NullReferenceException, and G# checks it (ArrayNullCheck)
    private static bool ReadsArrayThatMayBeNull(IPropertyReferenceOperation reference) =>
        reference.Property.ContainingType.SpecialType == SpecialType.System_Array &&
        reference.Instance is { Syntax: ExpressionSyntax receiver, SemanticModel: { } model } &&
        ArrayNullCheck.NeedsCheck(receiver, model);

    // (int)n of an int? n: C# raises InvalidOperationException when n has no value
    private static bool UnwrapsNullable(IConversionOperation conversion) =>
        IsNullable(conversion.Operand.Type) && !IsNullable(conversion.Type);

    private static bool IsNullable(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    // x / y and x % y of an integer type, as the divisor check decides it (DivisionCheckRule, the definition the
    // transpiler writes from): y == 0 raises DivideByZeroException; MinValue / -1, whose GPU result G# gives, is
    // counted with it. A native-sized integer (nint, nuint), which the rule has no range for, may fail unless its
    // divisor is a constant other than 0 and -1
    private static bool MayFailDivision(BinaryOperatorKind kind, ITypeSymbol? operatorType, IOperation dividend,
        IOperation divisor)
    {
        if (kind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder) || !IsInteger(operatorType))
        {
            return false;
        }

        var type = operatorType?.SpecialType ?? SpecialType.None;
        return IntegerRange.OfType(type) is null
            ? !IsConstantOtherThanZeroOrMinusOne(divisor)
            : DivisionCheckRule.MayThrow(IntegerSemantics.DecideDivision(dividend, divisor, type));
    }

    // x /= y and x %= y: the division of the operator type of the assignment (the promotion of the target and the
    // value)
    private static bool MayFailCompoundDivision(ICompoundAssignmentOperation compound)
    {
        if (compound.OperatorKind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder))
        {
            return false;
        }

        var type = IntegerSemantics.GetCompoundOperatorType(compound);
        if (type != SpecialType.None)
        {
            return DivisionCheckRule.MayThrow(IntegerSemantics.DecideDivision(compound.Target, compound.Value, type));
        }

        return (IsInteger(compound.Target.Type) || IsInteger(compound.Value.Type)) &&
               !IsConstantOtherThanZeroOrMinusOne(compound.Value);
    }

    private static bool IsConstantOtherThanZeroOrMinusOne(IOperation divisor) =>
        divisor.ConstantValue is { HasValue: true, Value: { } value } &&
        IntegerSemantics.TryGetInteger(value, out var constant) &&
        !constant.IsZero &&
        constant != BigInteger.MinusOne;

    private static bool IsInteger(ITypeSymbol? type) =>
        type?.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or
            SpecialType.System_UInt16 or SpecialType.System_Char or SpecialType.System_Int32 or
            SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

    // Math.Sqrt, MathF.Max, float.IsNaN ...: pure, and over floating-point values only, which raise nothing
    private static bool IsFloatingPointMath(IMethodSymbol method) =>
        DeviceIntrinsicEffects.IsPureIntrinsic(method) &&
        (IsFloatingPoint(method.ReturnType) || method.ReturnType.SpecialType == SpecialType.System_Boolean) &&
        method.Parameters
            .AsStackEnumerable()
            .All(static p => p.RefKind == RefKind.None && IsFloatingPoint(p.Type));

    private static bool IsFloatingPoint(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
}
