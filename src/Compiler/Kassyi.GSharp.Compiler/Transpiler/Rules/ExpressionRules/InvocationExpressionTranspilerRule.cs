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
    /// dialects alike), which the reduced form does not have. Only a call is decided here (GSH0003); a reduced
    /// extension method group without a call is GSH0062 (<c>SuspendingCallReference.FailIfReferencedWithoutCall</c>).
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

        // The test of a lowered user-defined && or ||: <operator false or true>([&]<left temporary>)
        if (node.Expression is IdentifierNameSyntax { Identifier.ValueText: GSharpInternalNames.Lowering.TruthTest })
        {
            var test = OperatorMacroSyntax.GetTruthTest(node, context.Query.SemanticModel)
                       ?? throw new InvalidOperationException(
                           "G#: internal error: a truth test without its operator (please report this G# bug).");
            MacroRules.GpuOperatorMacroTranspiler.AppendTruthTestCall(
                test,
                OperatorMacroCallPlanner.IsPassedByReference(test.Parameters[0]),
                node.ArgumentList.Arguments[0].Expression,
                static (argument, ref ctx) => ctx.Visitor.Visit(argument),
                ref context
            );
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

            // Before the custom rules, which write the warp intrinsics
            StructMemberSuspendingCall.FailIfInStructMember(node, symbol, ref context);
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
        if (symbol.ReducedFrom is { } extension &&
            node.Expression.UnwrapParentheses() is MemberAccessExpressionSyntax access &&
            CppNameResolver.IsEmittedDeviceFunction(extension, context.Emitter.Dialect))
        {
            var receiverPlan = ReceiverPlanner.PlanExtensionCall(access.Expression, extension,
                context.Query.SemanticModel);
            TranspileReceiverCall(node, symbol, extension, in receiverPlan, ref context);
            return;
        }

        if (!symbol.IsStatic && symbol.ReducedFrom == null &&
            CppNameResolver.IsEmittedDeviceFunction(symbol, context.Emitter.Dialect))
        {
            var receiverPlan = ReceiverPlanner.PlanInstanceCall(node, symbol, ref context);
            TranspileReceiverCall(node, symbol, symbol, in receiverPlan, ref context);
            return;
        }

        // The native stack form runs a warp collective through the prelude and passes the frame of a call that
        // reaches one (StackWarpEmitter)
        var stackWarp = StackWarpEmitter.Plan(node, symbol, ref context);

        // A method with no device definition (of the project, #891, or one the dialect does not map) would be
        // called by a name nothing defines
        // [WHY] Unsupported targets are either static or not emitted as instance device functions (those route to
        // TranspileReceiverCall above), so there is no receiver pointer expression to transform.
        if (stackWarp.Kind != StackWarpCallKind.Collective &&
            CallTargetEmitter.TryReplaceCall(node, symbol, ref context))
        {
            return;
        }

        StructOrdering.RegisterUsedStructIfApplicable(symbol.ReturnType, ref context);
        var convertArguments = CallTargetClassifier.ConvertsArguments(symbol, context.Emitter.Dialect);
        var checks = PlanArgumentChecks(symbol, node.ArgumentList.Arguments, ref context);
        using var sequence = EvaluationOrder.BeginCall(ref context, node.ArgumentList.Arguments, symbol,
            GetReceiver(node, symbol), spillAll: false);
        if (stackWarp.Kind == StackWarpCallKind.Collective)
        {
            context.Emitter.Writer.Append(stackWarp.Function);
        }
        else
        {
            context.Visitor.Visit(node.Expression);
        }

        using (context.Emitter.Writer.Parentheses())
        {
            if (stackWarp.Kind == StackWarpCallKind.Collective)
            {
                StackWarpEmitter.AppendCollectivePrefix(in stackWarp, symbol.Parameters.Length > 0, ref context);
            }

            TranspileArguments(symbol, node, in sequence, ref context, convertArguments, checks);
            DynamicSharedMemoryEmitter.AppendTableArgument(node, symbol, ref context);
            if (stackWarp.Kind == StackWarpCallKind.FrameCall)
            {
                StackWarpEmitter.AppendFrameArgument(in stackWarp, symbol.Parameters.Length > 0, ref context);
            }
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
                ? ArgumentNullCheck.PlanArgument(arguments[j].Expression, parameter, ref context)
                : ArgumentCheck.None);
        }

        return checks.MoveToImmutable();
    }

    // <function>(<receiver>, <arguments>): the call x.M(a) (or implicit M(a)) of an emitted struct instance
    // [GpuDeviceFunction] or reduced [GpuDeviceFunction] extension method is emitted as a free C++ function whose first
    // parameter is the receiver. When the receiver and arguments interfere, BeginReceiverCall sequences them in C# order.
    private static void TranspileReceiverCall(
        InvocationExpressionSyntax node,
        IMethodSymbol symbol,
        IMethodSymbol callee,
        in ReceiverPlan receiver,
        ref TranspileContext context)
    {
        if (symbol.ReducedFrom != null && FailIfSuspendingExtension(node, callee, ref context))
        {
            return;
        }

        var checks = PlanArgumentChecks(callee, node.ArgumentList.Arguments, ref context);
        StructOrdering.RegisterUsedStructIfApplicable(symbol.ReturnType, ref context);
        using var sequence = EvaluationOrder.BeginReceiverCall(ref context, in receiver, node.ArgumentList.Arguments,
            symbol);
        CppNameResolver.AppendSymbolName(callee, ref context);
        using (context.Emitter.Writer.Parentheses())
        {
            sequence.AppendReceiver(in receiver, ref context);

            context.Emitter.Writer.AppendIf(symbol.Parameters.Length > 0, ",");
            TranspileArguments(symbol, node, in sequence, ref context, checks: checks);
            DynamicSharedMemoryEmitter.AppendTableArgument(node, callee, ref context);
        }
    }

    // The receiver of an instance (or non-emitted reduced extension) call that stays on the standard invocation path.
    // [WHY] Emitted struct instance device functions and emitted [GpuDeviceFunction] extension methods route to
    // TranspileReceiverCall above; only un-emitted or intrinsic instance calls reach BeginCall here.
    private static ExpressionSyntax? GetReceiver(InvocationExpressionSyntax node, IMethodSymbol symbol) =>
        node.Expression.UnwrapParentheses() is MemberAccessExpressionSyntax access &&
        (!symbol.IsStatic || symbol.ReducedFrom != null)
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

        // The frame and the resume protocol follow the static call. A reduced extension call is unsupported (GSH0003);
        // a suspending instance [GpuDeviceFunction] is already reported with GSH0066 at its declaration.
        if (callee.ReducedFrom != null)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, call, SuspendingExtensionCall);
            context.Visitor.AppendUnsupportedPlaceholder(call);
            return;
        }

        if (!callee.IsStatic)
        {
            context.Visitor.AppendUnsupportedPlaceholder(call);
            return;
        }

        StructOrdering.RegisterUsedStructIfApplicable(callee.ReturnType, ref context);
        var checks = PlanArgumentChecks(callee, call.ArgumentList.Arguments, ref context);
        context.Visitor.Visit(call.Expression);
        using (context.Emitter.Writer.Parentheses())
        {
            // Lowering has evaluated the arguments into temporaries already (WarpInstructionRewriter.SpillArguments)
            TranspileArguments(callee, call, default, ref context, checks: checks);
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

    // A call that lowering synthesized (a prelude helper), as it is written; nameof and any other call without a method
    // never reach the output by name (CallTargetClassifier.ClassifyUntyped)
    private static void TranspileUntypedInvocation(
        InvocationExpressionSyntax node,
        ref TranspileContext context)
    {
        if (CallTargetEmitter.TryFailUntypedCall(node, ref context))
        {
            return;
        }

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
        InvocationExpressionSyntax call,
        in EvaluationOrder.CallSequence sequence,
        ref TranspileContext context,
        bool convertArguments = false,
        ImmutableArray<ArgumentCheck> checks = default)
    {
        var arguments = call.ArgumentList.Arguments;
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
                    context.Emitter.Writer.Append(ArgumentNullCheck.GetArgumentCheckHelper(check));
                    using (context.Emitter.Writer.Parentheses())
                    {
                        sequence.AppendArgument(arguments[j], parameter, ref context, convertArguments);
                        ArgumentNullCheck.AppendArgumentCheckTail(check, ref context);
                    }
                }

                hasArgument = true;
            }

            if (!hasArgument)
            {
                AppendOmittedArgument(parameter, call, ref context);
            }

            if (i < parameters.Length - 1)
            {
                context.Emitter.Writer.Append(',');
            }
        }
    }

    // An optional parameter the call omits: the call site's value of a caller-info parameter, its default value, or for
    // [Optional] without one default(T), as C# passes. A params parameter without arguments takes nothing
    private static void AppendOmittedArgument(IParameterSymbol parameter, InvocationExpressionSyntax call,
        ref TranspileContext context) =>
        OmittedArgument.Plan(parameter, call, context.Query.SemanticModel, visitsDefaultSyntax: true)
            .Append(parameter, ref context);
}
