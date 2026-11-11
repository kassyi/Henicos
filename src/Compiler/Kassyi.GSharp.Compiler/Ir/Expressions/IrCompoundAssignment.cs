using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A compound assignment <c>t op= v</c>; the target is evaluated once.</summary>
/// <remarks>
/// The value has the type of the target. The type the operator works in (<see langword="int"/> for <c>b += 1</c> with
/// a <see langword="byte"/> <c>b</c>) is not recorded: it follows from the C# numeric promotions and is decided after
/// the build.
/// </remarks>
/// <param name="operator">The operator.</param>
/// <param name="target">The location (<see cref="IrCategory.Location"/>).</param>
/// <param name="value">The right operand.</param>
/// <param name="inConversion">The conversion of the target's value to the operator's type.</param>
/// <param name="outConversion">The conversion of the result back to the target's type.</param>
/// <param name="category">How the holder uses the assignment's value.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrCompoundAssignment(IrOperator<BinaryOperatorKind> @operator, IrExpression target,
    IrExpression value, IrConversionKind inConversion, IrConversionKind outConversion, IrCategory category,
    IrSource source) : IrExpression(target.Type, category, default, source)
{
    /// <summary>The operator.</summary>
    public IrOperator<BinaryOperatorKind> Operator { get; } = @operator;

    /// <summary>The location.</summary>
    public IrExpression Target { get; } = target;

    /// <summary>The right operand.</summary>
    public IrExpression Value { get; } = value;

    /// <summary>The conversion of the target's value to the operator's type.</summary>
    public IrConversionKind InConversion { get; } = inConversion;

    /// <summary>The conversion of the result back to the target's type.</summary>
    public IrConversionKind OutConversion { get; } = outConversion;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrCompoundAssignment Update(IrExpression target, IrExpression value) =>
        target == Target && value == Value
            ? this
            : new IrCompoundAssignment(Operator, target, value, InConversion, OutConversion, Category, Source);
}
