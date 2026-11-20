using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Analysis.Checks;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Numeric;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// Whether evaluating an IR expression may raise a run-time error: ExpressionTraps over the IR.
/// </summary>
internal static class IrTraps
{
    public static bool MayTrap(IrExpression expression) => MayTrap(expression, IrTrapReading.Undecided);

    /// <summary>
    /// Whether evaluating <paramref name="expression"/> may raise a run-time error; with
    /// <see cref="IrTrapReading.Decided"/> the decisions of the nodes are read as well.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="reading"/> is <see cref="IrTrapReading.Decided"/> and a node it reads has not been decided.
    /// </exception>
    public static bool MayTrap(IrExpression expression, IrTrapReading reading) =>
        reading == IrTrapReading.Decided
            ? IrNodeChildren.Exists(expression, static node => node is IrExpression e && !IsTrapFreeDecided(e))
            : IrNodeChildren.Exists(expression, static node => node is IrExpression e && !IsTrapFree(e));

    public static bool MayTrapBeforeStore(IrExpression target) =>
        MayTrapBeforeStore(target, IrTrapReading.Undecided);

    /// <summary>
    /// Whether evaluating the assignment target <paramref name="target"/> may raise a run-time error before the value
    /// is stored (an array element is checked at the store); <paramref name="reading"/> as in
    /// <see cref="MayTrap(IrExpression, IrTrapReading)"/>.
    /// </summary>
    public static bool MayTrapBeforeStore(IrExpression target, IrTrapReading reading) =>
        target is IrElementReference { Receiver.Type: IArrayTypeSymbol } element
            ? MayTrap(element.Receiver, reading) || element.Index.Type?.SpecialType == SpecialType.System_UInt64 ||
              MayTrap(element.Index, reading)
            : MayTrap(target, reading);

    // A member of an array throws only when the decision checks its receiver; every other node follows the rules of
    // its kind
    private static bool IsTrapFreeDecided(IrExpression expression) => expression switch
    {
        // Another form is only a local sized by its declaration or a stack array: non-null, as the syntax rules prove
        IrPropertyReference { Property.ContainingType.SpecialType: SpecialType.System_Array } p =>
            ExpressionTraps.IsTrapFreeProperty(p.Property) &&
            p.Decided is not { Form: IrMemberForm.ArrayMember, ReceiverSite: not null },
        _ => IsTrapFree(expression)
    };

    private static bool IsTrapFree(IrExpression expression) => expression switch
    {
        IrConstant or IrVariableReference or IrFieldReference or IrDefaultValue or IrConditional or
            IrDiscard or IrInitializedObject => true,
        IrAssignment => true,
        IrLogical => true,
        IrNullTest => true,
        IrSequence => true,
        IrPropertyReference p =>
            ExpressionTraps.IsTrapFreeProperty(p.Property) &&
            p.Property.ContainingType.SpecialType != SpecialType.System_Array,
        IrBinary b =>
            b.Operator is { Method: null, IsChecked: false } &&
            (!ExpressionTraps.IsIntegerDivision(b.Operator.Kind, b.Type) || !MayFailDivision(b)),
        IrCompoundAssignment c =>
            c.Operator is { Method: null, IsChecked: false } &&
            c.InConversion != IrConversionKind.UserDefined &&
            c.OutConversion != IrConversionKind.UserDefined &&
            !MayFailCompoundDivision(c),
        IrIncrement i => i.OperatorMethod == null && !i.IsChecked,
        IrUnary u => u.Operator is { Method: null, IsChecked: false },
        IrConversion c =>
            c.Conversion is { Method: null, IsChecked: false } &&
            !ExpressionTraps.UnwrapsNullable(c.Operand.Type, c.Type),
        IrCall call => ExpressionTraps.IsFloatingPointMath(call.Method),
        _ => false
    };

    private static bool MayFailDivision(IrBinary binary)
    {
        var type = binary.Type?.SpecialType ?? SpecialType.None;
        if (IntegerRange.OfType(type) is null)
        {
            return ExpressionTraps.MayFailDivision(type, null, null, binary.Right.ConstantValue);
        }

        return ExpressionTraps.MayFailDivision(
            type, IrRanges.Of(binary.Left), IrRanges.Of(binary.Right), binary.Right.ConstantValue);
    }

    private static bool MayFailCompoundDivision(IrCompoundAssignment compound)
    {
        if (compound.Operator.Kind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder))
        {
            return false;
        }

        var type = IntegerSemantics.GetCompoundOperatorType(
            compound.Operator.Kind, compound.Target.Type, compound.Value.Type);
        if (type != SpecialType.None)
        {
            return ExpressionTraps.MayFailCompoundDivision(
                type, false, IrRanges.Of(compound.Target), IrRanges.Of(compound.Value), compound.Value.ConstantValue);
        }

        var targetOrValueIsInteger =
            ExpressionTraps.IsInteger(compound.Target.Type) || ExpressionTraps.IsInteger(compound.Value.Type);
        return ExpressionTraps.MayFailCompoundDivision(
            type, targetOrValueIsInteger, null, null, compound.Value.ConstantValue);
    }
}
