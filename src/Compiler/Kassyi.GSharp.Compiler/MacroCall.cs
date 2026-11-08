namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The call of an operator macro's target (<see cref="OperatorMacroCallPlanner.TryPrepareCall"/>): the target, its
/// context variable, and the result and operands with the parameters they pass to.
/// </summary>
internal readonly struct MacroCall(
    IMethodSymbol target,
    ISymbol? contextVariable,
    MacroOperand result,
    MacroOperand left,
    MacroOperand right,
    MacroOperand extra)
{
    /// <summary>The roles of the operands in the C# order of evaluation: result, left, right, extra.</summary>
    public static ImmutableArray<MacroOperandRole> OperandRoles { get; } = ImmutableArray.Create(
        MacroOperandRole.Result,
        MacroOperandRole.Left,
        MacroOperandRole.Right,
        MacroOperandRole.Extra
    );

    public IMethodSymbol Target { get; } = target;

    public ISymbol? ContextVariable { get; } = contextVariable;

    /// <summary>The assignment target, or (no expression) the temporary of an inner operator macro.</summary>
    public MacroOperand Result { get; } = result;

    public MacroOperand Left { get; } = left;

    public MacroOperand Right { get; } = right;

    /// <summary>The count of a shift.</summary>
    public MacroOperand Extra { get; } = extra;

    /// <summary>The operand in <paramref name="role"/> (not <see cref="MacroOperandRole.Context"/>).</summary>
    public MacroOperand GetOperand(MacroOperandRole role) => role switch
    {
        MacroOperandRole.Result => Result,
        MacroOperandRole.Left => Left,
        MacroOperandRole.Right => Right,
        MacroOperandRole.Extra => Extra,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "The context is not an operand.")
    };
}
