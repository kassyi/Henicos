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
    /// <inheritdoc />
    public override void Transpile(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        if (node.Expression is IdentifierNameSyntax { Identifier.ValueText: GSharpInternalNames.SuspendingCall.ResumeCall })
        {
            TranspileResumeCall(node, ref context);
            return;
        }

        if (context.Query.SemanticModel.GetSymbol(node) is IMethodSymbol symbol)
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
        foreach (var rule in context.RuleRegistry.InvocationRules)
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
        StructTranspiler.RegisterUsedStructIfApplicable(symbol.ReturnType, ref context);
        context.Visitor.Visit(node.Expression);
        using (context.Emitter.Writer.Parentheses())
        {
            TranspileArguments(symbol.Parameters, node.ArgumentList.Arguments, ref context);
            DynamicSharedMemoryEmitter.AppendTableArgument(node, symbol, ref context);
        }
    }

    // __gsharp_resume(Foo(args), n[, target]) becomes Foo(args, &__gsharp_ctx->__gsharp_call_frame_n, __gsharp_root[, &target]):
    // the callee resumes from its frame and returns whether it completed.
    private static void TranspileResumeCall(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        var arguments = node.ArgumentList.Arguments;
        if (arguments[0].Expression is not InvocationExpressionSyntax call ||
            arguments[1].Expression is not LiteralExpressionSyntax frameIndex ||
            context.Query.SemanticModel.GetSymbol(call) is not IMethodSymbol callee)
        {
            throw new InvalidOperationException("A lowered resume call does not wrap a call to a device function.");
        }

        StructTranspiler.RegisterUsedStructIfApplicable(callee.ReturnType, ref context);
        context.Visitor.Visit(call.Expression);
        using (context.Emitter.Writer.Parentheses())
        {
            TranspileArguments(callee.Parameters, call.ArgumentList.Arguments, ref context);
            if (callee.Parameters.Length > 0)
            {
                context.Emitter.Writer.Append(',');
            }

            context.Emitter.Writer.Append(
                $"&{GSharpInternalNames.StateMachine.Context}->{GSharpInternalNames.SuspendingCall.CallFramePrefix}{frameIndex.Token.ValueText}");
            context.Emitter.Writer.Append(',');
            context.Emitter.Writer.Append(GSharpInternalNames.SuspendingCall.Root);

            if (arguments.Count > 2)
            {
                context.Emitter.Writer.Append(',');
                context.Emitter.Writer.Append("&");
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Visitor.Visit(arguments[2].Expression);
                }
            }
        }
    }

    private static void TranspileUntypedInvocation(
        InvocationExpressionSyntax node,
        ref TranspileContext context)
    {
        context.Visitor.Visit(node.Expression);
        using (context.Emitter.Writer.Parentheses())
        {
            var arguments = node.ArgumentList.Arguments;
            for (var i = 0; i < arguments.Count; i++)
            {
                context.Visitor.Visit(arguments[i]);
                if (i < arguments.Count - 1)
                {
                    context.Emitter.Writer.Append(',');
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

            if (providedArg != null && parameter.RefKind is RefKind.In or RefKind.RefReadOnlyParameter)
            {
                // [WHY] An in or ref readonly parameter is a pointer in C++, as a ref parameter is, and C# accepts its
                // argument with in, ref or no modifier; the C++ argument is the address whichever was written.
                RefSyntaxHelper.TranspileAsByRefArgument(ref context, providedArg.Expression);
            }
            else if (providedArg != null)
            {
                context.Visitor.Visit(providedArg);

                if (parameter.IsParams && positionalArgIndex < arguments.Count)
                {
                    while (positionalArgIndex < arguments.Count && arguments[positionalArgIndex].NameColon == null)
                    {
                        context.Emitter.Writer.Append(',');
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
                context.Emitter.Writer.Append(',');
            }
        }
    }

    private static ArgumentSyntax? FindNamedArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        string parameterName) =>
        arguments
            .AsStackEnumerable()
            .FirstOrDefault(parameterName, static (arg, name) => arg.NameColon?.Name.Identifier.Text == name);

    private static void EmitDefaultValue(IParameterSymbol parameter, ref TranspileContext context)
    {
        if (GetDefaultValueSyntax(parameter) is { } syntax)
        {
            context.Visitor.Visit(syntax);
            return;
        }

        context.Emitter.Writer.Append(FormatConstantLiteral(parameter.ExplicitDefaultValue, parameter.Type));
    }

    private static ExpressionSyntax? GetDefaultValueSyntax(IParameterSymbol parameter) =>
        parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var paramSyntax) &&
        paramSyntax is { Default: { } defaultClause }
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

    private static string FormatFloat(float value) =>
        FormatNonFinite(double.IsNaN(value), float.IsPositiveInfinity(value), float.IsNegativeInfinity(value))
        ?? value.ToString(CultureInfo.InvariantCulture) + "f";

    private static string FormatDouble(double value) =>
        FormatNonFinite(double.IsNaN(value), double.IsPositiveInfinity(value), double.IsNegativeInfinity(value))
        ?? value.ToString(CultureInfo.InvariantCulture);

    private static string? FormatNonFinite(bool isNaN, bool isPosInf, bool isNegInf) =>
        isNaN ? "NAN" : isPosInf ? "INFINITY" : isNegInf ? "-INFINITY" : null;

    private static string GetIntegerSuffix(SpecialType specialType) => specialType switch
    {
        SpecialType.System_UInt32 => "u",
        SpecialType.System_Int64 => "ll",
        SpecialType.System_UInt64 => "ull",
        _ => string.Empty
    };
}
