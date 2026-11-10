namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>The argument of one parameter of a call, an indexer or a constructor.</summary>
/// <param name="parameter">The parameter.</param>
/// <param name="value">The value, with its conversion to the parameter type.</param>
/// <param name="kind">Whether the caller wrote it, or it is a default value or the <see langword="params"/>
/// array.</param>
/// <param name="evaluationIndex">The position of the argument in the C# order of evaluation.</param>
internal readonly struct IrArgument(IParameterSymbol parameter, IrExpression value, IrArgumentKind kind,
    int evaluationIndex)
{
    /// <summary>The parameter.</summary>
    public IParameterSymbol Parameter { get; } = parameter;

    /// <summary>The value, with its conversion to the parameter type.</summary>
    public IrExpression Value { get; } = value;

    /// <summary>Whether the caller wrote it, or it is a default value or the <see langword="params"/> array.</summary>
    public IrArgumentKind Kind { get; } = kind;

    /// <summary>
    /// The position of the argument in the C# order of evaluation: the order written, then the default values. Named
    /// arguments written out of parameter order make it differ from the parameter position.
    /// </summary>
    public int EvaluationIndex { get; } = evaluationIndex;
}
