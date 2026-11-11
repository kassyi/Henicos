using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// The invariants that one node keeps by itself, for <see cref="IrInvariants"/>. Each check returns the violation, a
/// line that starts with the number of the invariant, or <see langword="null"/>.
/// </summary>
internal static class IrNodeChecks
{
    /// <summary>I3, I4: a location is a variable, and the pointer category goes with a pointer type.</summary>
    public static string? Category(IrExpression expression, bool isWriteTarget)
    {
        if (expression.Category is IrCategory.Location or IrCategory.ReadOnlyLocation &&
            !IsLocation(expression, isWriteTarget))
        {
            return "I3: an expression that is not a variable is used as a location";
        }

        var isPointerType = expression.Type is IPointerTypeSymbol;
        return (expression.Category == IrCategory.Pointer && !isPointerType) ||
               (expression.Category == IrCategory.Value && isPointerType)
            ? "I4: the pointer category does not match the type"
            : null;
    }

    /// <summary>
    /// The node-specific invariants of an expression whose children are checked on their own: assignment targets
    /// (I5, I6), calls (I16), short-circuit operators and null tests (I11, I14), ref conditionals (I15), conversions
    /// (I18), sequences (I19).
    /// </summary>
    public static string? Node(IrExpression expression) => expression switch
    {
        IrAssignment assignment => Assignment(assignment),
        IrCompoundAssignment compound => Target(compound.Target),
        IrIncrement increment => Target(increment.Target),
        IrAddressOf address => Target(address.Operand),
        IrCall call => (call.Receiver is null) != call.Method.IsStatic
            ? "I16: a call has a receiver exactly when its method is not static"
            : null,
        IrBinary binary => Binary(binary),
        IrConditional conditional => Conditional(conditional),
        IrConversion conversion => Conversion(conversion),
        IrSequence sequence => sequence.Locals
            .AsStackEnumerable()
            .All(static local => local.Kind == IrVariableKind.Temporary)
            ? null
            : "I19: a sequence declares a variable that is not a temporary",
        _ => null
    };

    /// <summary>
    /// I7, I22: an argument has the category its parameter takes, and a <see langword="params"/> argument is the
    /// array created for it.
    /// </summary>
    public static string? Argument(IrArgument argument)
    {
        var category = argument.Value.Category;
        var matches = argument.Parameter.RefKind switch
        {
            RefKind.Ref or RefKind.Out => category == IrCategory.Location,
            RefKind.In or RefKind.RefReadOnlyParameter => category != IrCategory.Location,
            _ => category is IrCategory.Value or IrCategory.Pointer
        };
        if (!matches)
        {
            return $"I7: the argument of {argument.Parameter.Name} does not have the category its parameter takes";
        }

        return argument.Kind == IrArgumentKind.ParamArray &&
               (!argument.Parameter.IsParams || argument.Value is not (IrArrayCreation or IrOpaqueExpression))
            ? "I22: a params argument is not an array created for a params parameter"
            : null;
    }

    /// <summary>I21: a returned value has the category of the way the method returns.</summary>
    public static string? Return(IMethodSymbol method, IrExpression value)
    {
        var matches = method.RefKind switch
        {
            RefKind.Ref => value.Category == IrCategory.Location,
            RefKind.RefReadOnly => value.Category == IrCategory.ReadOnlyLocation,
            _ => value.Category is IrCategory.Value or IrCategory.Pointer
        };
        return matches ? null : "I21: the returned value's category does not match how the method returns";
    }

    private static bool IsLocation(IrExpression expression, bool isWriteTarget) => expression switch
    {
        IrVariableReference or IrElementReference or IrInitializedObject or IrDiscard or IrOpaqueExpression => true,
        IrFieldReference field => field.Field.IsStatic || field.Receiver is null or IrOpaqueExpression ||
                                  field.Receiver.Type.IsReferenceType ||
                                  field.Receiver.Category is IrCategory.Location or IrCategory.ReadOnlyLocation,
        IrPropertyReference property => property.Property.RefKind != RefKind.None ||
                                        (isWriteTarget && property.Category == IrCategory.Location),
        IrCall call => call.Method.RefKind != RefKind.None,
        IrConditional conditional => conditional.IsRef,
        _ => false
    };

    private static string? Target(IrExpression target) =>
        target.Category == IrCategory.Location ? null : "I5: an assigned or addressed operand is not a location";

    private static string? Assignment(IrAssignment assignment) =>
        Target(assignment.Target) ??
        (assignment.IsRef && assignment.Value.Category is not (IrCategory.Location or IrCategory.ReadOnlyLocation)
            ? "I6: the value of a ref assignment is not a location"
            : null);

    private static string? Binary(IrBinary binary)
    {
        if (binary.Operator.Kind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
        {
            return "I11: a short-circuit operator is a binary node, not a logical one";
        }

        return binary.Operator.Kind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals &&
               binary.Operator.Method is null &&
               IsNullConstant(binary.Left) != IsNullConstant(binary.Right)
            ? "I14: a built-in comparison with null is not a null test"
            : null;
    }

    private static bool IsNullConstant(IrExpression expression) =>
        expression.ConstantValue is { HasValue: true, Value: null };

    // A ref conditional passes its use on to the branches (a location, or a value when it is read)
    private static string? Conditional(IrConditional conditional)
    {
        var armsMatch = conditional.IsRef
            ? conditional.WhenTrue.Category == conditional.Category &&
              conditional.WhenFalse.Category == conditional.Category
            : conditional.WhenTrue.Category is IrCategory.Value or IrCategory.Pointer &&
              conditional.WhenFalse.Category is IrCategory.Value or IrCategory.Pointer;
        return armsMatch ? null : "I15: the branches of a conditional do not have the category its kind requires";
    }

    private static string? Conversion(IrConversion conversion)
    {
        // The type of an opaque operand is not known to the IR (a collection expression, a switch expression)
        if (conversion.Operand is IrOpaqueExpression)
        {
            return null;
        }

        var from = conversion.Operand.Type;
        var to = conversion.Type;
        var kind = conversion.Conversion.Kind;
        if (kind == IrConversionKind.Identity && !WithoutTupleNames(from).IsEqualTo(WithoutTupleNames(to)))
        {
            return "I18: an identity conversion converts between different types";
        }

        return kind is IrConversionKind.Numeric or IrConversionKind.Enumeration or IrConversionKind.ImplicitConstant &&
               !(IrConversionKinds.IsNumericOrEnum(from) && IrConversionKinds.IsNumericOrEnum(to))
            ? "I18: a numeric or enum conversion has an operand or result of another type"
            : null;
    }

    // An identity conversion may rename the elements of a tuple: (int, int) to (int A, int B)
    private static ITypeSymbol WithoutTupleNames(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } underlying } ? underlying : type;
}
