using Kassyi.GSharp.Compiler.Analysis.Checks;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// The values an IR expression can take: <see cref="OperationRanges.Of"/> over the IR (the same rules, in the same
/// order).
/// </summary>
internal static class IrRanges
{
    /// <summary>The values <paramref name="operand"/> can take; null for an operand that is not an integer.</summary>
    public static IntegerRange? Of(IrExpression operand) => Of(operand, null);

    /// <summary>
    /// The values <paramref name="operand"/> can take, resolving temporary variable references through
    /// <paramref name="temporaries"/> when provided; null for an operand that is not an integer.
    /// </summary>
    public static IntegerRange? Of(IrExpression operand, IrTemporaryInitializers? temporaries)
    {
        operand = temporaries?.Resolve(operand) ?? operand;
        if (operand.ConstantValue is { HasValue: true, Value: { } value } &&
            IntegerSemantics.TryGetInteger(value, out var constant))
        {
            return IntegerRange.Constant(constant);
        }

        if (OperationRanges.OfType(operand.Type) is not { } range)
        {
            return null;
        }

        if (operand is IrConversion { Conversion.Method: null } conversion &&
            Of(conversion.Operand, temporaries) is { } inner && inner.Min >= range.Min && inner.Max <= range.Max)
        {
            return inner;
        }

        if (operand is IrPropertyReference { Receiver: not null, Property: var lengthProperty } &&
            OperationRanges.IsLength(lengthProperty))
        {
            return new IntegerRange(0, range.Max);
        }

        if (operand is IrPropertyReference { Receiver: null, Property: var intrinsicProperty } &&
            OperationRanges.OfLaunchIntrinsic(intrinsicProperty) is { } launch)
        {
            return launch;
        }

        return range;
    }
}
