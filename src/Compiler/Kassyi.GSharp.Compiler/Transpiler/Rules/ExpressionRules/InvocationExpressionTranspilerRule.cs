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
            // C# removes the call of a partial method without an implementing declaration, or of a [Conditional]
            // method whose symbols are not defined, arguments included; a call that is not a whole statement (the body
            // of an expression-bodied lambda or local function, a for incrementor) leaves an expression that does
            // nothing (#1424)
            if (RemovedCalls.GetUserTree(node, context.Query.SemanticModel) is { } tree &&
                RemovedCalls.IsRemoved(symbol, tree))
            {
                context.Emitter.Writer.Append("((void)0)");
                return;
            }

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

        // A method of the project with no device definition would be called by a name nothing defines (#891)
        if (UnemittedMethodCall.GetReason(symbol, node, context.Query.SemanticModel, context.Emitter.Dialect) is
            { } reason)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode, node,
                UnemittedMethodCall.GetDisplayName(symbol), reason);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        StructOrdering.RegisterUsedStructIfApplicable(symbol.ReturnType, ref context);
        var convertArguments = context.Emitter.Dialect.TryMapIntrinsic(symbol, out _) &&
                               !symbol.HasAttribute(KnownAttributes.CudaIntrinsic) &&
                               symbol.ContainingType?.HasAttribute(KnownAttributes.CudaIntrinsicPattern) != true;
        var checks = PlanArgumentChecks(symbol, node.ArgumentList.Arguments, ref context);
        using var sequence = EvaluationOrder.BeginCall(ref context, node.ArgumentList.Arguments, symbol,
            GetReceiver(node, symbol), spillAll: false);
        context.Visitor.Visit(node.Expression);
        using (context.Emitter.Writer.Parentheses())
        {
            TranspileArguments(symbol, node.ArgumentList.Arguments, in sequence, ref context, convertArguments, checks);
            DynamicSharedMemoryEmitter.AppendTableArgument(node, symbol, ref context);
        }
    }

    // The check of each argument of a call of a device function that the parameter's nullable annotation asks for
    // (#1409): the callee reads a non-nullable array parameter without a check, so the caller checks what it has not
    // proven. Empty for any other callee
    private static ImmutableArray<ArgumentCheck> PlanArgumentChecks(IMethodSymbol callee,
        SeparatedSyntaxList<ArgumentSyntax> arguments, ref TranspileContext context)
    {
        if (!CppNameResolver.IsEmittedDeviceFunction(callee, context.Emitter.Dialect) || callee.IsExtensionMethod ||
            !NonNullProof.ChecksArguments(callee))
        {
            return ImmutableArray<ArgumentCheck>.Empty;
        }

        var checks = ImmutableArray.CreateBuilder<ArgumentCheck>(arguments.Count);
        for (var j = 0; j < arguments.Count; j++)
        {
            checks.Add(callee.FindParameter(arguments, j) is { } parameter
                ? ArrayNullCheck.PlanArgument(arguments[j].Expression, parameter, ref context)
                : ArgumentCheck.None);
        }

        return checks.MoveToImmutable();
    }

    // <function>(<receiver>, <arguments>): the reduced call x.M(a) of a [GpuDeviceFunction] extension method is the
    // static call M(x, a). When the receiver and arguments interfere, BeginExtensionCall sequences them in C# order.
    private static void TranspileExtensionCall(InvocationExpressionSyntax node, ExpressionSyntax receiver,
        IMethodSymbol symbol, IMethodSymbol extension, ref TranspileContext context)
    {
        if (FailIfSuspendingExtension(node, extension, ref context))
        {
            return;
        }

        var model = context.Query.SemanticModel;
        var byReference = extension.Parameters[0].RefKind != RefKind.None;

        StructOrdering.RegisterUsedStructIfApplicable(symbol.ReturnType, ref context);
        using var sequence = EvaluationOrder.BeginExtensionCall(ref context, receiver, extension.Parameters[0],
            node.ArgumentList.Arguments, symbol);
        CppNameResolver.AppendSymbolName(extension, ref context);
        using (context.Emitter.Writer.Parentheses())
        {
            sequence.AppendReceiver(receiver, byReference, ref context);

            context.Emitter.Writer.AppendIf(symbol.Parameters.Length > 0, ",");
            TranspileArguments(symbol, node.ArgumentList.Arguments, in sequence, ref context);
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

        StructOrdering.RegisterUsedStructIfApplicable(callee.ReturnType, ref context);
        var checks = PlanArgumentChecks(callee, call.ArgumentList.Arguments, ref context);
        context.Visitor.Visit(call.Expression);
        using (context.Emitter.Writer.Parentheses())
        {
            // Lowering has evaluated the arguments into temporaries already (WarpInstructionRewriter.SpillArguments)
            TranspileArguments(callee, call.ArgumentList.Arguments, default, ref context, checks: checks);
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

    // The arguments in the order of the parameters, each the argument that C# binds to the parameter: the one that
    // names it, or the one at its position (a positional argument may follow a named one in its own position,
    // F(a: 1, 5)). A params parameter takes every argument it binds; an omitted optional parameter takes its default
    // value
    private static void TranspileArguments(
        IMethodSymbol callee,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        in EvaluationOrder.CallSequence sequence,
        ref TranspileContext context,
        bool convertArguments = false,
        ImmutableArray<ArgumentCheck> checks = default)
    {
        var parameters = callee.Parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var hasArgument = false;
            for (var j = 0; j < arguments.Count; j++)
            {
                if (callee.FindParameter(arguments, j)?.Ordinal != i)
                {
                    continue;
                }

                context.Emitter.Writer.AppendIf(hasArgument, ",");
                // The argument itself, or the temporary it was evaluated into (an in or ref readonly parameter by
                // address), checked when the parameter's annotation asks for it
                var check = checks.IsDefaultOrEmpty ? ArgumentCheck.None : checks[j];
                if (check.Kind == ArgumentCheckKind.None)
                {
                    sequence.AppendArgument(arguments[j], parameter, ref context, convertArguments);
                }
                else
                {
                    context.Emitter.Writer.Append(ArrayNullCheck.GetArgumentCheckHelper(check));
                    using (context.Emitter.Writer.Parentheses())
                    {
                        sequence.AppendArgument(arguments[j], parameter, ref context, convertArguments);
                        ArrayNullCheck.AppendArgumentCheckTail(check, ref context);
                    }
                }

                hasArgument = true;
            }

            if (!hasArgument)
            {
                AppendOmittedArgument(parameter, ref context);
            }

            if (i < parameters.Length - 1)
            {
                context.Emitter.Writer.Append(',');
            }
        }
    }

    // An optional parameter the call omits: its default value, or for [Optional] without one default(T), as C# passes.
    // A params parameter without arguments takes nothing
    private static void AppendOmittedArgument(IParameterSymbol parameter, ref TranspileContext context)
    {
        if (parameter.HasExplicitDefaultValue)
        {
            EmitDefaultValue(parameter, ref context);
        }
        else if (parameter.IsOptional)
        {
            context.Emitter.Writer.Append("{}");
        }
    }

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
