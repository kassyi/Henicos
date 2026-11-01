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
    /// <summary>The GSH0003 text for an extension method call whose receiver and arguments interfere.</summary>
    public const string ExtensionReceiverInterferes =
        "extension method call whose arguments write what its receiver reads, or read what it writes";

    /// <summary>The GSH0003 text for an extension method call of a device function that suspends at a barrier.</summary>
    public const string SuspendingExtensionCall = "extension method call of a device function that reaches a barrier";

    /// <summary>
    /// Reports <see cref="SuspendingExtensionCall"/> and returns <see langword="true"/> when <paramref name="extension"/>
    /// reaches a barrier: a suspending call passes its frame after the arguments of the static call (lowering, both
    /// dialects alike), which the reduced form does not have.
    /// </summary>
    internal static bool FailIfSuspendingExtension(SyntaxNode node, IMethodSymbol extension, ref TranspileContext context)
    {
        if (!SuspensionAnalysis.IsSuspendingFunction(extension, context.Query.SemanticModel.Compilation))
        {
            return false;
        }

        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, SuspendingExtensionCall);
        return true;
    }

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
        if (symbol.ReducedFrom is { } extension && node.Expression is MemberAccessExpressionSyntax access &&
            CppNameResolver.IsEmittedDeviceFunction(extension, context.Emitter.Dialect))
        {
            TranspileExtensionCall(node, access.Expression, symbol, extension, ref context);
            return;
        }

        StructTranspiler.RegisterUsedStructIfApplicable(symbol.ReturnType, ref context);
        using var sequence = EvaluationOrder.BeginCall(ref context, node.ArgumentList.Arguments, symbol,
            GetReceiver(node, symbol), spillAll: false);
        context.Visitor.Visit(node.Expression);
        using (context.Emitter.Writer.Parentheses())
        {
            TranspileArguments(symbol.Parameters, node.ArgumentList.Arguments, in sequence, ref context);
            DynamicSharedMemoryEmitter.AppendTableArgument(node, symbol, ref context);
        }
    }

    // <function>(<receiver>, <arguments>): the reduced call x.M(a) of a [GpuDeviceFunction] extension method is the
    // static call M(x, a). [WHY] C++ leaves the order of the arguments unspecified and the temporaries of BeginCall are
    // evaluated before the receiver, so a receiver that interferes with an argument is reported instead of reordered.
    private static void TranspileExtensionCall(InvocationExpressionSyntax node, ExpressionSyntax receiver,
        IMethodSymbol symbol, IMethodSymbol extension, ref TranspileContext context)
    {
        if (FailIfSuspendingExtension(node, extension, ref context))
        {
            return;
        }

        var model = context.Query.SemanticModel;
        var byReference = extension.Parameters[0].RefKind != RefKind.None;
        var receiverEffects = byReference ? ExpressionEffects.OfLocation(receiver, model) : ExpressionEffects.Of(receiver, model);
        var interferes = node.ArgumentList.Arguments
            .AsStackEnumerable()
            .Any((Receiver: receiverEffects, Model: model), static (argument, s) =>
                ExpressionEffects.Of(argument.Expression, s.Model) is var effects &&
                (s.Receiver.InterferesWith(effects) || effects.InterferesWith(s.Receiver)));
        if (interferes)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, ExtensionReceiverInterferes);
            return;
        }

        StructTranspiler.RegisterUsedStructIfApplicable(symbol.ReturnType, ref context);
        using var sequence = EvaluationOrder.BeginCall(ref context, node.ArgumentList.Arguments, symbol, receiver: null,
            spillAll: false);
        CppNameResolver.AppendSymbolName(extension, ref context);
        using (context.Emitter.Writer.Parentheses())
        {
            if (byReference)
            {
                RefSyntaxHelper.TranspileAsByRefArgument(ref context, receiver);
            }
            else
            {
                context.Visitor.Visit(receiver);
            }

            context.Emitter.Writer.AppendIf(symbol.Parameters.Length > 0, ",");
            TranspileArguments(symbol.Parameters, node.ArgumentList.Arguments, in sequence, ref context);
            DynamicSharedMemoryEmitter.AppendTableArgument(node, extension, ref context);
        }
    }

    // The receiver of an instance (or reduced extension) call, which C# and C++ evaluate before the arguments.
    // [WHY] G# emits neither the instance methods of a struct nor reduced extension calls of other methods today (those of
    // a [GpuDeviceFunction] go through TranspileExtensionCall), so the receiver path (and its ReducedFrom branch) is not
    // reached by valid output; when the receiver interferes with an argument,
    // BeginCall leaves the whole call as written, which also leaves the arguments in C++ order (to be fixed with the
    // instance methods).
    private static ExpressionSyntax? GetReceiver(InvocationExpressionSyntax node, IMethodSymbol symbol) =>
        node.Expression is MemberAccessExpressionSyntax access && (!symbol.IsStatic || symbol.ReducedFrom != null)
            ? access.Expression
            : null;

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

        // The frame and the resume protocol follow the static call; the reduced form has its receiver out of the arguments
        if (callee.ReducedFrom != null)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, call, SuspendingExtensionCall);
            return;
        }

        StructTranspiler.RegisterUsedStructIfApplicable(callee.ReturnType, ref context);
        context.Visitor.Visit(call.Expression);
        using (context.Emitter.Writer.Parentheses())
        {
            // Lowering has evaluated the arguments into temporaries already (WarpInstructionRewriter.SpillArguments)
            TranspileArguments(callee.Parameters, call.ArgumentList.Arguments, default, ref context);
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
        var arguments = node.ArgumentList.Arguments;
        using var sequence = EvaluationOrder.BeginCall(ref context, arguments, callee: null, node.Expression,
            spillAll: false);
        context.Visitor.Visit(node.Expression);
        using (context.Emitter.Writer.Parentheses())
        {
            for (var i = 0; i < arguments.Count; i++)
            {
                sequence.AppendArgument(arguments[i], parameter: null, ref context);
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
        in EvaluationOrder.CallSequence sequence,
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
                // The argument itself, or the temporary it was evaluated into (an in or ref readonly parameter by address)
                sequence.AppendArgument(providedArg, parameter, ref context);

                if (parameter.IsParams && positionalArgIndex < arguments.Count)
                {
                    while (positionalArgIndex < arguments.Count && arguments[positionalArgIndex].NameColon == null)
                    {
                        context.Emitter.Writer.Append(',');
                        sequence.AppendArgument(arguments[positionalArgIndex], parameter, ref context);
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

        // No default clause in the syntax: [DefaultParameterValue], or a method from metadata
        context.Emitter.Writer.Append(ConstexprLiteral.FormatCppConstant(parameter.Type, parameter.ExplicitDefaultValue));
    }

    private static ExpressionSyntax? GetDefaultValueSyntax(IParameterSymbol parameter) =>
        parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var paramSyntax) &&
        paramSyntax is { Default: { } defaultClause }
            ? defaultClause.Value
            : null;
}
