using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Proves whether an IR array or struct expression cannot be <see langword="null"/>, matching
/// <see cref="NonNullProof"/> and <see cref="ArrayNullCheck"/> by calling their symbol-based helpers (#1561).
/// </summary>
internal static class IrNonNullProof
{
    /// <summary>Whether reading through <paramref name="expression"/> needs a null check.</summary>
    public static bool NeedsCheck(IrExpression expression, IrDecisionContext context) =>
        ArrayNullCheck.IsCheckedType(expression.Type) && MayBeNull(expression, context);

    /// <summary>Whether <paramref name="expression"/> may evaluate to <see langword="null"/>.</summary>
    public static bool MayBeNull(IrExpression expression, IrDecisionContext context)
    {
        expression = UnwrapImplicitReferenceConversions(expression);
        if (expression is IrConditional conditional)
        {
            return MayBeNull(conditional.WhenTrue, context) || MayBeNull(conditional.WhenFalse, context);
        }

        if (IsProvenNonNull(expression, context))
        {
            return false;
        }

        return !ArrayNullCheck.IsHostStaticField((expression as IrFieldReference)?.Field);
    }

    /// <summary>Whether <paramref name="expression"/>, of an array type, is proven non-null.</summary>
    public static bool IsProvenNonNull(IrExpression expression, IrDecisionContext context)
    {
        expression = UnwrapImplicitReferenceConversions(expression);
        if (IsStackArrayReference(expression))
        {
            return true;
        }

        return expression switch
        {
            IrConditional conditional =>
                IsProvenNonNull(conditional.WhenTrue, context) && IsProvenNonNull(conditional.WhenFalse, context),
            IrFieldReference { Receiver: { } receiver, Field: var field } =>
                NonNullProof.IsProvenFieldSymbol(field) && IsProvenStruct(receiver, context),
            IrVariableReference
            {
                Variable: { Kind: IrVariableKind.Parameter, Symbol: IParameterSymbol parameter }
            } =>
                NonNullProof.IsNonNullableArray(parameter.Type, parameter.NullableAnnotation) &&
                NonNullProof.IsCheckedParameter(parameter, context.Compilation),
            IrVariableReference
            {
                Variable: { Kind: IrVariableKind.Local, Symbol: ILocalSymbol local } variable
            } =>
                NonNullProof.IsUnwrittenLocalSymbol(local, context.Compilation) &&
                context.Initializers.TryGet(variable, out var initializer) &&
                IsProvenNonNull(initializer, context),
            IrCall call => NonNullProof.IsNotNullMethod(call.Method),
            _ => false
        };
    }

    /// <summary>
    /// Whether <paramref name="expression"/>, of a struct type, holds a non-null array in every non-nullable array
    /// field reachable through struct fields.
    /// </summary>
    public static bool IsProvenStruct(IrExpression expression, IrDecisionContext context)
    {
        expression = UnwrapImplicitReferenceConversions(expression);
        return expression switch
        {
            IrFieldReference { Receiver: { } receiver, Field: var field } =>
                NonNullProof.IsProvenStructFieldSymbol(field) && IsProvenStruct(receiver, context),
            IrVariableReference
            {
                Variable:
                {
                    Kind: IrVariableKind.Parameter,
                    Symbol: IParameterSymbol { Type.IsValueType: true } parameter
                }
            } =>
                NonNullProof.IsCheckedParameter(parameter, context.Compilation),
            IrVariableReference
            {
                Variable:
                {
                    Kind: IrVariableKind.Local,
                    Symbol: ILocalSymbol { Type.IsValueType: true } local
                } variable
            } =>
                NonNullProof.IsUnwrittenLocalSymbol(local, context.Compilation) &&
                context.Initializers.TryGet(variable, out var initializer) &&
                IsProvenStruct(initializer, context),
            _ => false
        };
    }

    /// <summary>
    /// Whether <paramref name="expression"/> (<c>Gpu.NotNull</c> and implicit reference conversions aside) names a
    /// stack array local.
    /// </summary>
    public static bool IsStackArrayReference(IrExpression expression)
    {
        while (true)
        {
            expression = UnwrapImplicitReferenceConversions(expression);
            if (expression is IrCall { Arguments: [{ } only] } call && NonNullProof.IsNotNullMethod(call.Method))
            {
                expression = only.Value;
                continue;
            }

            break;
        }

        return expression is IrVariableReference { Variable.Symbol: ILocalSymbol local } &&
               StackArrayLocals.IsStackArray(local, out _);
    }

    /// <summary>Unwraps compiler-synthesized implicit reference conversions on <paramref name="expression"/>.</summary>
    internal static IrExpression UnwrapImplicitReferenceConversions(IrExpression expression)
    {
        while (expression is IrConversion
               {
                   IsImplicit: true,
                   Conversion: { Method: null, Kind: IrConversionKind.Reference }
               } conversion)
        {
            expression = conversion.Operand;
        }

        return expression;
    }
}
