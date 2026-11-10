namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A conditional expression <c>c ? a : b</c>, or <c>c ? ref a : ref b</c>.</summary>
/// <remarks>Both branches are converted to the type of the expression, which is the type of each branch.</remarks>
/// <param name="condition">The condition.</param>
/// <param name="whenTrue">The branch when the condition holds.</param>
/// <param name="whenFalse">The branch when it does not.</param>
/// <param name="isRef">
/// Whether the branches are references (<c>ref</c> conditional); then they have the category of the expression.
/// </param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="constantValue">The constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrConditional(IrExpression condition, IrExpression whenTrue, IrExpression whenFalse, bool isRef,
    IrCategory category, Optional<object?> constantValue, IrSource source)
    : IrExpression(whenTrue.Type, category, constantValue, source)
{
    /// <summary>The condition.</summary>
    public IrExpression Condition { get; } = condition;

    /// <summary>The branch when the condition holds.</summary>
    public IrExpression WhenTrue { get; } = whenTrue;

    /// <summary>The branch when it does not.</summary>
    public IrExpression WhenFalse { get; } = whenFalse;

    /// <summary>Whether the branches are references (<c>ref</c> conditional).</summary>
    public bool IsRef { get; } = isRef;
}
