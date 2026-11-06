namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles the return statement to CUDA C++.</summary>
internal static class ReturnStatementTranspilers
{
    /// <summary>Where a <c>return</c> goes, decided from the emitter state before anything is written.</summary>
    private enum ReturnKind
    {
        /// <summary>A return of a <c>Gpu.SingleThread</c> body: <c>goto</c> the end of the body.</summary>
        SingleThreadEnd,

        /// <summary>A return in a suspending device function.</summary>
        SuspendingFunction,

        /// <summary>A kernel entry returning a value: it goes to the result buffer.</summary>
        KernelResultBuffer,

        /// <summary>A return of a value from a callable that returns it.</summary>
        Value,

        /// <summary>A return in a struct constructor, which returns the constructed object.</summary>
        ConstructorSelf,

        /// <summary>A plain <c>return;</c>.</summary>
        Void
    }

    /// <summary>
    /// Transpiles a return statement, hoisting any inline declarations from the returned expression.
    /// </summary>
    public static void TranspileReturnStatement(ReturnStatementSyntax node, ref TranspileContext context)
    {
        var isStateMachineReturn = node.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind);
        var kind = Classify(node, ref context);

        switch (kind)
        {
            case ReturnKind.SingleThreadEnd:
                // The end label is set whenever this kind is chosen
                context.Emitter.Writer.Append($"goto {context.Emitter.SingleThreadEndLabel};");
                return;
            case ReturnKind.SuspendingFunction:
                TranspileSuspendingDeviceFunctionReturn(node, isStateMachineReturn, ref context);
                return;
        }

        if (node.Expression != null)
        {
            StatementPrelude.Emit(node.Expression, ref context);
        }

        if (kind == ReturnKind.KernelResultBuffer)
        {
            context.Emitter.Writer.Append(GSharpInternalNames.ReturnValue.Buffer);
            using (context.Emitter.Writer.Brackets())
            {
                context.Emitter.Writer.Append(GSharpInternalNames.ReturnValue.GlobalThreadIndex);
            }

            context.Emitter.Writer.Append(" = ");
            context.Visitor.Visit(node.Expression);
            context.Emitter.Writer.AppendLine(";");
        }

        var emitStateTermination = !isStateMachineReturn && context.Emitter.Dialect.EmulatesThreadsWithStateMachine()
                                   && context.Emitter is { IsGpuKernelEntry: true, CallableDepth: 0 };
        if (emitStateTermination)
        {
            context.Emitter.Writer.Append(GSharpInternalNames.StateMachine.Termination);
        }

        switch (kind)
        {
            case ReturnKind.Value:
                context.Emitter.Writer.Append("return ");
                context.Visitor.Visit(node.Expression);
                context.Emitter.Writer.Append(';');
                break;
            case ReturnKind.ConstructorSelf:
                context.Emitter.Writer.Append($"return {GSharpInternalNames.Lowering.ConstructorSelf};");
                break;
            default:
                context.Emitter.Writer.Append("return;");
                break;
        }
    }

    private static ReturnKind Classify(ReturnStatementSyntax node, ref TranspileContext context)
    {
        if (context.Emitter.SingleThreadEndLabel != null &&
            context.Emitter.CallableDepth == context.Emitter.SingleThreadCallableDepth)
        {
            return ReturnKind.SingleThreadEnd;
        }

        if (context.Emitter is { IsSuspendingDeviceFunction: true, CallableDepth: 0 })
        {
            return ReturnKind.SuspendingFunction;
        }

        if (node.Expression != null)
        {
            return context.Emitter is { IsGpuKernelEntry: true, IsVoidReturn: false, CallableDepth: 0 }
                ? ReturnKind.KernelResultBuffer
                : ReturnKind.Value;
        }

        return context.Emitter is { StructConstructorType: not null, CallableDepth: 1 }
            ? ReturnKind.ConstructorSelf
            : ReturnKind.Void;
    }

    // A suspension reports "not completed"; a return of the user's code writes the result, resets the frame for the next
    // call and reports completion.
    private static void TranspileSuspendingDeviceFunctionReturn(ReturnStatementSyntax node, bool isStateMachineReturn,
        ref TranspileContext context)
    {
        if (isStateMachineReturn)
        {
            context.Emitter.Writer.Append("return false;");
            return;
        }

        if (node.Expression != null)
        {
            StatementPrelude.Emit(node.Expression, ref context);
            context.Emitter.Writer.Append($"*{GSharpInternalNames.SuspendingCall.Result} = ");
            context.Visitor.Visit(node.Expression);
            context.Emitter.Writer.AppendLine(";");
        }

        context.Emitter.Writer.Append($"{GSharpInternalNames.StateMachine.State} = 0;");
        context.Emitter.Writer.Append("return true;");
    }
}
