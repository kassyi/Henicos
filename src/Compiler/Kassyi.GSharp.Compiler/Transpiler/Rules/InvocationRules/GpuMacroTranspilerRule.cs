namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Invocation transpiler rule that handles Gpu.InlinePtx and Gpu.Constant macro expansions, the grid barrier of
/// Gpu.SyncGrid(mode) and the null check of Gpu.NotNull(array).
/// </summary>
internal sealed class GpuMacroTranspilerRule : IInvocationTranspilerRule
{

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        var call = GpuMacroClassifier.Classify(symbol);
        switch (call.Kind)
        {
            case GpuMacroKind.InlinePtx:
                TranspileInlinePtx(node, ref context);
                return true;

            case GpuMacroKind.Constant:
                TranspileConstant(node, call, ref context);
                return true;

            case GpuMacroKind.NotNull when call.GetArgument(node.ArgumentList.Arguments, 0) is { } array:
                NotNullCallCheck.Append(NotNullCallCheck.Plan(node, array.Expression, ref context), ref context);
                return true;

            case GpuMacroKind.SyncGrid when !call.HasBody:
                TranspileGridSync(node, call, ref context);
                return true;

            default:
                return false;
        }
    }

    // __gsharp_grid_sync() | __gsharp_grid_sync_fast(): the barrier of the constant mode, with no argument. [WHY] The
    // mode selects the code of the barrier, not a value it reads, so no run-time branch is emitted. The CPU dialects
    // lower the call into a state of the kernel (StateMachineLowerer) before it gets here
    private static void TranspileGridSync(
        InvocationExpressionSyntax node,
        GpuMacroCall call,
        ref TranspileContext context)
    {
        var barrier = GridSyncBarriers.Get(node, call, context.Query.SemanticModel, out var modeArgument);
        switch (barrier)
        {
            case GridSyncBarrier.Deterministic:
                context.Emitter.Writer.Append($"{GSharpInternalNames.Prelude.GridSync}()");
                return;
            case GridSyncBarrier.Fast:
                context.Emitter.Writer.Append($"{GSharpInternalNames.Prelude.GridSyncFast}()");
                return;
            case GridSyncBarrier.NotConstant:
                context.Reporter.Fail(GSharpDiagnostics.MacroArgumentMustBeConstant, (SyntaxNode?)modeArgument ?? node,
                    GridSyncBarriers.ModeParameter, GridSyncBarriers.MacroName);
                return;
            default:
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, (SyntaxNode?)modeArgument ?? node,
                    GridSyncBarriers.UndefinedMode);
                return;
        }
    }

    private static void TranspileInlinePtx(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        if (node.ArgumentList.Arguments.Count != 1)
        {
            return;
        }

        // The analyzer warns (GSH0054); the call becomes a runtime failure
        if (!DialectCallSupport.SupportsInlinePtx(context.Emitter.Dialect))
        {
            var dialectName = context.Emitter.Dialect.Name;
            context.Emitter.Writer.Append($"{GSharpInternalNames.Prelude.Unsupported}(\"{DialectCallSupport.GetInlinePtxUnsupportedMessage(dialectName)}\");");
            context.Emitter.Writer.AppendLine();
            if (!context.Emitter.Dialect.EmulatesThreadsWithStateMachine() || !context.Emitter.IsGpuKernelEntry)
            {
                return;
            }

            context.Emitter.Writer.AppendLine(GSharpInternalNames.StateMachine.Termination);
            context.Emitter.Writer.AppendLine("return;");
            return;
        }

        var argExpr = node.ArgumentList.Arguments[0].Expression;

        if (!GpuCodeConstraints.IsInlinePtxText(argExpr, context.Query.SemanticModel))
        {
            context.Reporter.Fail(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, argExpr);
            return;
        }

        if (context.Query.SemanticModel.GetSafeConstantValue(argExpr) is { HasValue: true, Value: string ptxString })
        {
            context.Emitter.Writer.Append(ptxString);
            context.Emitter.Writer.AppendLine();
            return;
        }

        // IsInlinePtxText leaves only an interpolated string that is not constant
        TranspileInterpolatedInlinePtx((InterpolatedStringExpressionSyntax)argExpr, ref context);
    }

    private static void TranspileInterpolatedInlinePtx(InterpolatedStringExpressionSyntax interpolated,
        ref TranspileContext context)
    {
        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    context.Emitter.Writer.Append(text.TextToken.ValueText);
                    break;
                case InterpolationSyntax interpolation:
                    context.Visitor.Visit(interpolation.Expression);
                    break;
            }
        }

        context.Emitter.Writer.AppendLine();
    }

    // <macro name>: the value of the constant string argument (a literal, nameof, a const field, a concatenation of
    // constants). Any other argument is a failure (GSH0012), never an empty expression or the C# name of a field; an
    // argument C# cannot bind is the C# compiler's error
    private static void TranspileConstant(InvocationExpressionSyntax node, GpuMacroCall call,
        ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (call.GetArgument(node.ArgumentList.Arguments, 0)?.Expression is not { } argument)
        {
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        if (model.GetSafeConstantValue(argument) is { HasValue: true, Value: string macroName })
        {
            context.Emitter.Writer.Append(macroName);
            return;
        }

        if (model.GetTypeSymbol(argument) is { TypeKind: not TypeKind.Error } && call.Method is { } method)
        {
            context.Reporter.Fail(GSharpDiagnostics.MacroArgumentMustBeConstant, argument,
                method.Parameters[0].Name, method.Name);
        }

        context.Visitor.AppendUnsupportedPlaceholder(node);
    }
}
