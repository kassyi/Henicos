namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>An increment or decrement, prefix or postfix; the target is evaluated once.</summary>
/// <remarks>The value has the type of the target.</remarks>
/// <param name="target">The location (<see cref="IrCategory.Location"/>).</param>
/// <param name="isPrefix">Whether the value is the one after the step (<c>++x</c>).</param>
/// <param name="isDecrement">Whether the step is <c>--</c>.</param>
/// <param name="operatorMethod">The user-defined operator; <see langword="null"/> for a built-in one.</param>
/// <param name="isChecked">Whether the step is in a <see langword="checked"/> context.</param>
/// <param name="category">How the holder uses the value.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrIncrement(IrExpression target, bool isPrefix, bool isDecrement, IMethodSymbol? operatorMethod,
    bool isChecked, IrCategory category, IrSource source) : IrExpression(target.Type, category, default, source)
{
    /// <summary>The location.</summary>
    public IrExpression Target { get; } = target;

    /// <summary>Whether the value is the one after the step (<c>++x</c>).</summary>
    public bool IsPrefix { get; } = isPrefix;

    /// <summary>Whether the step is <c>--</c>.</summary>
    public bool IsDecrement { get; } = isDecrement;

    /// <summary>The user-defined operator; <see langword="null"/> for a built-in one.</summary>
    public IMethodSymbol? OperatorMethod { get; } = operatorMethod;

    /// <summary>Whether the step is in a <see langword="checked"/> context.</summary>
    public bool IsChecked { get; } = isChecked;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrIncrement Update(IrExpression target) =>
        target == Target
            ? this
            : new IrIncrement(target, IsPrefix, IsDecrement, OperatorMethod, IsChecked, Category, Source);
}
