using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Whether an operand that C# evaluates before a split can be read where it stands, after the split, instead of
/// being evaluated into a temporary before it.
/// </summary>
/// <remarks>
/// [WHY] Only what no one can change across the split stays: a constant, or a local or by-value parameter that has
/// no other name (<see cref="AliasedVariables"/>), which no other thread and no callee can reach, and that nothing
/// evaluated between it and its use writes. A span or other ref struct, which no temporary keeps across a split, stays
/// unless something after it writes it. The same rule as <c>SpilledArguments.CanStayInPlace</c> (the arguments of a
/// call that splits) and <c>SpilledArguments.IsSpilledLeftOperand</c> (a left operand), applied to every operand.
/// </remarks>
internal static class IrStayInPlace
{
    /// <summary>
    /// Whether <paramref name="operand"/> stays in place.
    /// </summary>
    /// <param name="operand">The operand, evaluated for its value.</param>
    /// <param name="others">What the operands that could change it read and write.</param>
    /// <param name="later">What the operands evaluated after it read and write (a ref struct stays unless these write
    /// it).</param>
    /// <param name="compilation">The compilation.</param>
    public static bool Stays(IrExpression operand, in ExpressionEffects others, in ExpressionEffects later,
        Compilation compilation)
    {
        var value = WithoutBuiltInImplicitConversions(operand);
        if (operand.ConstantValue.HasValue || value.ConstantValue.HasValue)
        {
            return true;
        }

        if (value is not IrVariableReference { Variable: var variable } reference)
        {
            return false;
        }

        var read = IrEffects.Of(reference, compilation);
        return variable.IsRefLike
            ? !read.InterferesWith(later)
            : IsUnaliased(variable, compilation) && !read.InterferesWith(others);
    }

    /// <summary>What evaluating <paramref name="operand"/> as its holder uses it reads and writes.</summary>
    public static ExpressionEffects EffectsOf(IrExpression operand, Compilation compilation) => operand.Category switch
    {
        IrCategory.Location => IrEffects.OfWrittenLocation(operand, compilation),
        IrCategory.ReadOnlyLocation => IrEffects.OfLocation(operand, compilation, readsValue: true),
        _ => IrEffects.Of(operand, compilation)
    };

    /// <summary>
    /// <paramref name="expression"/> without the implicit conversions that C# inserted and that run no user code,
    /// which read nothing besides their operand.
    /// </summary>
    public static IrExpression WithoutBuiltInImplicitConversions(IrExpression expression)
    {
        while (expression is IrConversion { IsImplicit: true, Conversion.Method: null } conversion)
        {
            expression = conversion.Operand;
        }

        return expression;
    }

    /// <summary>
    /// Whether <paramref name="receiver"/> is a local declared as a stack array, which is a location.
    /// </summary>
    public static bool IsStackArray(IrExpression receiver) =>
        WithoutBuiltInImplicitConversions(receiver) is IrVariableReference { Variable.Symbol: ILocalSymbol local } &&
        StackArrayLocals.IsStackArray(local, out _);

    // A local or by-value parameter that no other name reaches; a temporary of a pass has none
    private static bool IsUnaliased(IrVariable variable, Compilation compilation) => variable.Kind switch
    {
        IrVariableKind.Temporary => true,
        IrVariableKind.Local or IrVariableKind.Parameter when variable.RefKind == RefKind.None &&
                                                              variable.Symbol is { } symbol =>
            !AliasedVariables.Contains(symbol, compilation),
        _ => false
    };
}
