namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Reorders invocation arguments to match parameter positions and provides fallback expressions for omitted
/// optional parameters.
/// </summary>
internal static class ArgumentMatchingHelper
{
    /// <summary>
    /// Matches invocation arguments to the parameters of the callee. Index <c>i</c> holds the argument that parameter
    /// <c>i</c> receives, or <c>null</c> when the call omits the optional parameter.
    /// </summary>
    public static ArgumentSyntax?[] MatchArgumentsToParameters(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol callee)
    {
        var parameters = callee.Parameters;
        var matched = new ArgumentSyntax?[parameters.Length];
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var index = argument.NameColon is { } nameColon
                ? parameters
                    .AsStackEnumerable()
                    .IndexOf(nameColon.Name.Identifier.ValueText, static (parameter, name) => parameter.Name == name)
                : i;
            if (index < 0 || index >= matched.Length)
            {
                throw new InvalidOperationException(
                    $"An argument of a call does not match a parameter of '{callee.Name}'.");
            }

            matched[index] = argument;
        }

        return matched;
    }

    /// <summary>
    /// Creates the fallback expression for an omitted optional parameter: literal, casted enum literal, or default.
    /// </summary>
    public static ExpressionSyntax CreateDefaultArgument(IParameterSymbol parameter)
    {
        // [Optional] without [DefaultParameterValue]: the caller passes default(T)
        if (!parameter.HasExplicitDefaultValue)
        {
            return parameter.IsOptional
                ? SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)
                : throw new InvalidOperationException(
                    $"A call omits the parameter '{parameter.Name}', which has no default value.");
        }

        var value = parameter.ExplicitDefaultValue;
        var literal = value switch
        {
            null => SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression),
            bool flag => SyntaxFactory.LiteralExpression(flag
                ? SyntaxKind.TrueLiteralExpression
                : SyntaxKind.FalseLiteralExpression),
            float single when float.IsNaN(single) || float.IsInfinity(single) =>
                SyntaxFactory.ParseExpression(GetNonFiniteName("float", single)),
            double number when double.IsNaN(number) || double.IsInfinity(number) =>
                SyntaxFactory.ParseExpression(GetNonFiniteName("double", number)),
            float single => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(single)),
            double number => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(number)),
            // A literal would be -2147483648, which C++ reads as the negation of a literal that does not fit int. The
            // expression is outside the compilation, so int.MinValue would bind to nothing; the arithmetic form needs no
            // binding and is the same constant in C# and C++
            int.MinValue => CreateMinValue(SyntaxFactory.Literal(int.MaxValue), SyntaxFactory.Literal(1)),
            long.MinValue => CreateMinValue(SyntaxFactory.Literal(long.MaxValue), SyntaxFactory.Literal(1L)),
            _ => CreateIntegralLiteral(value)
        };

        if (parameter.Type.TypeKind != TypeKind.Enum)
        {
            return literal;
        }

        using var writer = new SourceWriter();
        writer.AppendCsharpTypeName(parameter.Type);
        return SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName(writer.ToString()), literal);
    }

    // (-<max> - 1)
    private static ExpressionSyntax CreateMinValue(SyntaxToken max, SyntaxToken one) =>
        SyntaxFactory.ParenthesizedExpression(
            SyntaxFactory.BinaryExpression(
                SyntaxKind.SubtractExpression,
                SyntaxFactory.PrefixUnaryExpression(
                    SyntaxKind.UnaryMinusExpression,
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, max)
                ),
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, one)
            )
        );

    private static string GetNonFiniteName(string type, double value) =>
        type + (double.IsNaN(value) ? ".NaN" : value > 0 ? ".PositiveInfinity" : ".NegativeInfinity");

    private static LiteralExpressionSyntax CreateIntegralLiteral(object value)
    {
        var token = value switch
        {
            int number => SyntaxFactory.Literal(number),
            uint number => SyntaxFactory.Literal(number),
            long number => SyntaxFactory.Literal(number),
            ulong number => SyntaxFactory.Literal(number),
            char character => SyntaxFactory.Literal(character),
            byte or sbyte or short or ushort => SyntaxFactory.Literal(
                Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException(
                $"A call omits a parameter whose default value '{value}' has no C# literal.")
        };

        return SyntaxFactory.LiteralExpression(value is char
            ? SyntaxKind.CharacterLiteralExpression
            : SyntaxKind.NumericLiteralExpression, token);
    }
}
