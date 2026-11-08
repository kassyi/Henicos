namespace Kassyi.GSharp.Compiler;

/// <summary>
/// One operand of the call of an operator macro's target (<see cref="MacroCall"/>) and the parameter it passes to.
/// </summary>
internal readonly struct MacroOperand(ExpressionSyntax? expression, IParameterSymbol? parameter)
{
    /// <summary>
    /// The operand as written; <see langword="null"/> for the result when it is the temporary of an inner operator
    /// macro, and when the operator has no such operand.
    /// </summary>
    public ExpressionSyntax? Expression { get; } = expression;

    /// <summary>
    /// The parameter of the target it passes to; <see langword="null"/> when the operator has no such operand. The
    /// result always has one, also when it is a temporary.
    /// </summary>
    public IParameterSymbol? Parameter { get; } = parameter;
}
