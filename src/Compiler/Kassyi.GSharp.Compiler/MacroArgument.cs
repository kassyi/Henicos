using static Kassyi.GSharp.Compiler.OperatorMacroResolver;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// One argument of the call of an operator macro's target, as <see cref="OperatorMacroCallPlanner.PlanArguments"/>
/// decides it: what it is, the parameter it passes to, how, the type it converts to, and whether it is evaluated into a
/// temporary first. The C++ emitter and the managed expansion write it as it is.
/// </summary>
internal readonly struct MacroArgument(
    MacroArgumentKind kind,
    IParameterSymbol parameter,
    MacroOperandRole role,
    ExpressionSyntax? operand,
    NestedOperatorMacro? nested,
    ITypeSymbol? operandType,
    bool spills)
{
    public MacroArgumentKind Kind { get; } = kind;

    /// <summary>The parameter of the target that the argument passes to.</summary>
    public IParameterSymbol Parameter { get; } = parameter;

    /// <summary>The place of the argument in the call: the context, the result or an operand.</summary>
    public MacroOperandRole Role { get; } = role;

    /// <summary>The operand as written; <see langword="null"/> for the context and an inner result.</summary>
    public ExpressionSyntax? Operand { get; } = operand;

    /// <summary>The operator macro that a <see cref="MacroArgumentKind.NestedOperand"/> is.</summary>
    public NestedOperatorMacro? Nested { get; } = nested;

    /// <summary>The type of <see cref="Operand"/>, when it has one.</summary>
    public ITypeSymbol? OperandType { get; } = operandType;

    /// <summary>
    /// The parameter type when the operand's type is another one: the value converts to it, implicitly in C++ and
    /// through a cast on the managed path; <see langword="null"/> when no conversion applies.
    /// </summary>
    public ITypeSymbol? ConversionType =>
        OperandType is { } type && !type.IsEqualTo(Parameter.Type) ? Parameter.Type : null;

    /// <summary>Whether the parameter takes the argument by reference.</summary>
    public bool IsPassedByReference => OperatorMacroCallPlanner.IsPassedByReference(Parameter);

    /// <summary>
    /// Whether the operand is evaluated into a temporary before the call, to keep the C# order of evaluation
    /// (<see cref="EvaluationOrder.LastSpilledOperand"/>).
    /// </summary>
    public bool Spills { get; } = spills;
}
