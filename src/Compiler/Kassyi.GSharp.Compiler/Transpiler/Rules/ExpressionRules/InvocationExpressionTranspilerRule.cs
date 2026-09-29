using System.Globalization;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles method invocation syntax nodes into target dialect function calls,
/// coordinating with BCL intrinsic mappings and GPU macro expansions before defaulting to standard call emission.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class InvocationExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<InvocationExpressionSyntax>
{
    /// <summary>
    /// Specialized invocation transpiler rules evaluated before falling back to standard call emission.
    /// </summary>
    private static readonly ImmutableArray<IInvocationTranspilerRule> s_invocationRules =
        ImmutableArray.Create<IInvocationTranspilerRule>(
            new BclIntrinsicTranspilerRule(),
            new GpuMacroTranspilerRule()
        );

    /// <inheritdoc />
    public override void Transpile(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        if (context.SemanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol symbol)
        {
            if (TryTranspileCustomInvocation(node, symbol, ref context))
            {
                return;
            }

            TranspileStandardInvocation(node, symbol, ref context);
            return;
        }

        TranspileUntypedInvocation(node, ref context);
    }

    private static bool TryTranspileCustomInvocation(
        InvocationExpressionSyntax node,
        IMethodSymbol symbol,
        ref TranspileContext context)
    {
        foreach (var rule in s_invocationRules)
        {
            if (rule.TryTranspile(node, ref context, symbol))
            {
                return true;
            }
        }

        return false;
    }

    private static void TranspileStandardInvocation(
        InvocationExpressionSyntax node,
        IMethodSymbol symbol,
        ref TranspileContext context)
    {
        context.Visitor.Visit(node.Expression);
        using (context.Writer.Parentheses())
        {
            TranspileArguments(symbol.Parameters, node.ArgumentList.Arguments, ref context);
        }
    }

    private static void TranspileUntypedInvocation(
        InvocationExpressionSyntax node,
        ref TranspileContext context)
    {
        context.Visitor.Visit(node.Expression);
        using (context.Writer.Parentheses())
        {
            var arguments = node.ArgumentList.Arguments;
            for (var i = 0; i < arguments.Count; i++)
            {
                context.Visitor.Visit(arguments[i]);
                if (i < arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }

    private static void TranspileArguments(
        ImmutableArray<IParameterSymbol> parameters,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ref TranspileContext context)
    {
        var positionalArgIndex = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var providedArg = FindNamedArgument(arguments, parameter.Name);

            if (providedArg == null && positionalArgIndex < arguments.Count &&
                arguments[positionalArgIndex].NameColon == null)
            {
                providedArg = arguments[positionalArgIndex];
                positionalArgIndex++;
            }

            if (providedArg != null)
            {
                context.Visitor.Visit(providedArg);

                if (parameter.IsParams && positionalArgIndex < arguments.Count)
                {
                    while (positionalArgIndex < arguments.Count && arguments[positionalArgIndex].NameColon == null)
                    {
                        context.Writer.AppendComma();
                        context.Visitor.Visit(arguments[positionalArgIndex]);
                        positionalArgIndex++;
                    }
                }
            }
            else if (parameter.HasExplicitDefaultValue)
            {
                EmitDefaultValue(parameter, ref context);
            }

            if (i < parameters.Length - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }

    private static ArgumentSyntax? FindNamedArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        string parameterName)
    {
        foreach (var arg in arguments)
        {
            if (arg.NameColon != null && arg.NameColon.Name.Identifier.Text == parameterName)
            {
                return arg;
            }
        }

        return null;
    }

    private static void EmitDefaultValue(IParameterSymbol parameter, ref TranspileContext context)
    {
        if (GetDefaultValueSyntax(parameter) is { } syntax)
        {
            context.Visitor.Visit(syntax);
            return;
        }

        context.Writer.Append(FormatConstantLiteral(parameter.ExplicitDefaultValue, parameter.Type));
    }

    private static ExpressionSyntax? GetDefaultValueSyntax(IParameterSymbol parameter) =>
        parameter.DeclaringSyntaxReferences.Length > 0 &&
        parameter.DeclaringSyntaxReferences[0].GetSyntax() is ParameterSyntax { Default: { } defaultClause }
            ? defaultClause.Value
            : null;

    private static string FormatConstantLiteral(object? value, ITypeSymbol type) => value switch
    {
        null => "nullptr",
        bool b => b ? "true" : "false",
        float f => FormatFloat(f),
        double d => FormatDouble(d),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) +
                                    GetIntegerSuffix(type.SpecialType),
        _ => (value.ToString() ?? string.Empty) + GetIntegerSuffix(type.SpecialType)
    };

    private static string FormatFloat(float value) => value switch
    {
        float.NaN => "NAN",
        _ when float.IsPositiveInfinity(value) => "INFINITY",
        _ when float.IsNegativeInfinity(value) => "-INFINITY",
        _ => value.ToString(CultureInfo.InvariantCulture) + "f"
    };

    private static string FormatDouble(double value) => value switch
    {
        double.NaN => "NAN",
        _ when double.IsPositiveInfinity(value) => "INFINITY",
        _ when double.IsNegativeInfinity(value) => "-INFINITY",
        _ => value.ToString(CultureInfo.InvariantCulture)
    };

    private static string GetIntegerSuffix(SpecialType specialType) => specialType switch
    {
        SpecialType.System_UInt32 => "u",
        SpecialType.System_Int64 => "L",
        SpecialType.System_UInt64 => "UL",
        _ => string.Empty
    };
}
