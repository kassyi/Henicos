namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// The value that C# passes for a caller-info parameter (<c>[CallerLineNumber]</c>, <c>[CallerFilePath]</c>,
/// <c>[CallerMemberName]</c>, <c>[CallerArgumentExpression]</c>) that a call omits: the line, file, member or argument
/// text of the call site, not the parameter's default value (#946). Every place that writes an omitted argument (calls,
/// constructors, suspending calls, inlined iterators, the managed rewrite) takes it from here.
/// </summary>
/// <remarks>
/// [WHY] The C++ output and the generated managed C# pass every omitted argument themselves, or call from another
/// file, so the value that C# computes at the user's call site has to be passed explicitly. It is the value the C#
/// compiler binds at the call's original site (the default-value argument of the call's operation), so C#'s rules hold
/// as they are: which attribute counts, the token whose line counts, <c>#line</c> and <c>/pathmap</c>. The call may be
/// a node of a lowered tree; the site is its original node.
/// </remarks>
internal static class CallerInfoArgument
{
    /// <summary>
    /// The value C# passes to <paramref name="parameter"/> when <paramref name="call"/> omits it, converted to the
    /// parameter type (a number, or a <see cref="string"/>); <see langword="null"/> when the parameter is not a
    /// caller-info parameter or the call passes it.
    /// </summary>
    public static object? GetValue(IParameterSymbol parameter, SyntaxNode call, SemanticModel semanticModel)
    {
        if (!parameter.IsOptional || !HasCallerInfoAttribute(parameter) ||
            !semanticModel.TryGetOriginalNode(call, out var site, out var siteModel))
        {
            return null;
        }

        var arguments = siteModel.GetOperation(site) switch
        {
            IInvocationOperation invocation => invocation.Arguments,
            IObjectCreationOperation creation => creation.Arguments,
            IPropertyReferenceOperation indexer => indexer.Arguments,
            _ => ImmutableArray<IArgumentOperation>.Empty
        };
        foreach (var argument in arguments)
        {
            if (argument is { ArgumentKind: ArgumentKind.DefaultValue, Parameter: { } bound } &&
                bound.Ordinal == parameter.Ordinal &&
                GetConstant(argument.Value) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// The literal of the value <see cref="GetValue"/> gives, as an expression that device code can hold;
    /// <see langword="null"/> when the parameter takes its default value or the value is a string, which has no device
    /// form.
    /// </summary>
    public static LiteralExpressionSyntax? CreateDeviceArgument(IParameterSymbol parameter, SyntaxNode call,
        SemanticModel semanticModel) =>
        GetValue(parameter, call, semanticModel) is { } value and not string ? CreateLiteral(value) : null;

    /// <summary>The C# literal of a value that <see cref="GetValue"/> returned.</summary>
    public static LiteralExpressionSyntax CreateLiteral(object value)
    {
        var token = value switch
        {
            string text => SyntaxFactory.Literal(text),
            int number => SyntaxFactory.Literal(number),
            uint number => SyntaxFactory.Literal(number),
            long number => SyntaxFactory.Literal(number),
            ulong number => SyntaxFactory.Literal(number),
            float number => SyntaxFactory.Literal(number),
            double number => SyntaxFactory.Literal(number),
            decimal number => SyntaxFactory.Literal(number),
            _ => throw new InvalidOperationException($"A caller-info value '{value}' has no C# literal.")
        };
        return SyntaxFactory.LiteralExpression(value is string
            ? SyntaxKind.StringLiteralExpression
            : SyntaxKind.NumericLiteralExpression, token);
    }

    // The constant, or for a nullable or object parameter the constant that the conversion wraps
    private static object? GetConstant(IOperation value) =>
        value.ConstantValue is { HasValue: true, Value: { } constant } ? constant
        : value is IConversionOperation { Operand.ConstantValue: { HasValue: true, Value: { } operand } } ? operand
        : null;

    private static bool HasCallerInfoAttribute(IParameterSymbol parameter) =>
        parameter.HasAttribute(KnownAttributes.CallerLineNumber) ||
        parameter.HasAttribute(KnownAttributes.CallerFilePath) ||
        parameter.HasAttribute(KnownAttributes.CallerMemberName) ||
        parameter.HasAttribute(KnownAttributes.CallerArgumentExpression);
}
