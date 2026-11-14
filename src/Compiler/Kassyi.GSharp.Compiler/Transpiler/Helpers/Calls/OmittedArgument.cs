namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

/// <summary>
/// What the C++ output passes for an optional parameter that a call omits, decided before anything is written
/// (<see cref="Plan"/>) and written from that decision alone (<see cref="Append"/>): the call site's value of a
/// caller-info parameter (<see cref="CallerInfoArgument"/>), the default clause as written, the default value, or
/// <c>default(T)</c>.
/// </summary>
/// <param name="Kind">Which of the sources the argument comes from.</param>
/// <param name="Constant">
/// The value of <see cref="OmittedArgumentKind.CallerInfo"/> and <see cref="OmittedArgumentKind.Constant"/>.
/// </param>
/// <param name="DefaultSyntax">The default clause of <see cref="OmittedArgumentKind.DefaultSyntax"/>.</param>
internal readonly record struct OmittedArgument(
    OmittedArgumentKind Kind,
    object? Constant = null,
    ExpressionSyntax? DefaultSyntax = null)
{
    /// <summary>
    /// Decides the argument of <paramref name="parameter"/>, which <paramref name="call"/> omits (no call: the default
    /// value only). <paramref name="visitsDefaultSyntax"/> is set for a method call: a default clause of the declaration
    /// is then written as the expression it is, so that a constant such as <c>float.NaN</c> keeps its form, and a
    /// <c>params</c> parameter takes nothing; a constructor or a mapped call writes the default value.
    /// </summary>
    public static OmittedArgument Plan(IParameterSymbol parameter, SyntaxNode? call, SemanticModel semanticModel,
        bool visitsDefaultSyntax)
    {
        if (call is not null && CallerInfoArgument.GetDeviceValue(parameter, call, semanticModel) is { } callerInfo)
        {
            return new OmittedArgument(OmittedArgumentKind.CallerInfo, callerInfo);
        }

        if (!visitsDefaultSyntax)
        {
            return parameter.ExplicitDefaultValue is { } value
                ? new OmittedArgument(OmittedArgumentKind.Constant, value)
                : new OmittedArgument(OmittedArgumentKind.DefaultOfType);
        }

        // A method call: the default clause as written, else the default value; a params parameter takes nothing
        if (parameter.HasExplicitDefaultValue)
        {
            return parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var declaration) &&
                   declaration is { Default.Value: { } defaultSyntax }
                ? new OmittedArgument(OmittedArgumentKind.DefaultSyntax, DefaultSyntax: defaultSyntax)
                : new OmittedArgument(OmittedArgumentKind.Constant, parameter.ExplicitDefaultValue);
        }

        return parameter.IsOptional
            ? new OmittedArgument(OmittedArgumentKind.DefaultOfType)
            : new OmittedArgument(OmittedArgumentKind.None);
    }

    /// <summary>Writes the argument as <see cref="Plan"/> decided it for <paramref name="parameter"/>.</summary>
    public void Append(IParameterSymbol parameter, ref TranspileContext context)
    {
        switch (Kind)
        {
            case OmittedArgumentKind.DefaultSyntax when DefaultSyntax is not null:
                context.Visitor.Visit(DefaultSyntax);
                break;
            case OmittedArgumentKind.CallerInfo or OmittedArgumentKind.Constant:
                context.Emitter.Writer.Append(ConstexprLiteral.FormatCppConstant(parameter.Type, Constant));
                break;
            case OmittedArgumentKind.DefaultOfType:
                context.Emitter.Writer.Append("{}");
                break;
        }
    }
}

/// <summary>Where the argument of an omitted optional parameter comes from (<see cref="OmittedArgument"/>).</summary>
internal enum OmittedArgumentKind
{
    /// <summary>The value C# passes for a caller-info parameter at the call site.</summary>
    CallerInfo,

    /// <summary>The default clause of the parameter's declaration, as written.</summary>
    DefaultSyntax,

    /// <summary>The default value as a constant (<c>[DefaultParameterValue]</c>, or a method from metadata).</summary>
    Constant,

    /// <summary><c>default(T)</c>: <c>[Optional]</c> without a value, or a default of <c>null</c>.</summary>
    DefaultOfType,

    /// <summary>Nothing: a <c>params</c> parameter without arguments.</summary>
    None
}
