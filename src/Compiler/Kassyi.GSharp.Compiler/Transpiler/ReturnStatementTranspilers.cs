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

    /// <summary>Everything a return statement needs to be written, decided before the first write.</summary>
    /// <param name="Kind">The shape of the return.</param>
    /// <param name="EmitsStateTermination">Whether the state-machine termination is written before the return.</param>
    private readonly record struct ReturnPlan(ReturnKind Kind, bool EmitsStateTermination);

    /// <summary>
    /// Transpiles a return statement, hoisting any inline declarations from the returned expression.
    /// </summary>
    public static void TranspileReturnStatement(ReturnStatementSyntax node, ref TranspileContext context)
    {
        var isStateMachineReturn = node.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind);
        var plan = Classify(node, isStateMachineReturn, ref context);

        switch (plan.Kind)
        {
            case ReturnKind.SingleThreadEnd:
                WriteSingleThreadEnd(ref context);
                break;
            case ReturnKind.SuspendingFunction:
                TranspileSuspendingDeviceFunctionReturn(node, isStateMachineReturn, ref context);
                break;
            case ReturnKind.KernelResultBuffer:
                WriteKernelResultBuffer(node, plan, ref context);
                break;
            case ReturnKind.Value:
                WriteValue(node, plan, ref context);
                break;
            case ReturnKind.ConstructorSelf:
                WriteConstructorSelf(plan, ref context);
                break;
            default:
                WriteVoid(plan, ref context);
                break;
        }
    }

    private static ReturnPlan Classify(
        ReturnStatementSyntax node,
        bool isStateMachineReturn,
        ref TranspileContext context
    )
    {
        if (context.Emitter.SingleThreadEndLabel != null &&
            context.Emitter.CallableDepth == context.Emitter.SingleThreadCallableDepth)
        {
            return new ReturnPlan(ReturnKind.SingleThreadEnd, false);
        }

        if (context.Emitter is { IsSuspendingDeviceFunction: true, CallableDepth: 0 })
        {
            return new ReturnPlan(ReturnKind.SuspendingFunction, false);
        }

        var emitsStateTermination = !isStateMachineReturn && context.Emitter.Dialect.EmulatesThreadsWithStateMachine()
                                    && context.Emitter is { IsGpuKernelEntry: true, CallableDepth: 0 };

        if (node.Expression != null)
        {
            var kind = context.Emitter is { IsGpuKernelEntry: true, IsVoidReturn: false, CallableDepth: 0 }
                ? ReturnKind.KernelResultBuffer
                : ReturnKind.Value;
            return new ReturnPlan(kind, emitsStateTermination);
        }

        var noValueKind = context.Emitter is { StructConstructorType: not null, CallableDepth: 1 }
            ? ReturnKind.ConstructorSelf
            : ReturnKind.Void;
        return new ReturnPlan(noValueKind, emitsStateTermination);
    }

    // The end label is set whenever this kind is chosen
    private static void WriteSingleThreadEnd(ref TranspileContext context) =>
        context.Emitter.Writer.Append($"goto {context.Emitter.SingleThreadEndLabel};");

    // <result buffer>[<global thread index>] = <value>; the termination; return;
    private static void WriteKernelResultBuffer(
        ReturnStatementSyntax node,
        ReturnPlan plan,
        ref TranspileContext context
    )
    {
        if (node.Expression is { } expression)
        {
            StatementPrelude.Emit(expression, ref context);
        }

        context.Emitter.Writer.Append(GSharpInternalNames.ReturnValue.Buffer);
        using (context.Emitter.Writer.Brackets())
        {
            context.Emitter.Writer.Append(GSharpInternalNames.ReturnValue.GlobalThreadIndex);
        }

        context.Emitter.Writer.Append(" = ");
        context.Visitor.Visit(node.Expression);
        context.Emitter.Writer.AppendLine(";");

        WriteStateTermination(plan, ref context);
        context.Emitter.Writer.Append("return;");
    }

    // <termination> return <value>;
    private static void WriteValue(ReturnStatementSyntax node, ReturnPlan plan, ref TranspileContext context)
    {
        if (node.Expression is { } expression)
        {
            StatementPrelude.Emit(expression, ref context);
        }

        WriteStateTermination(plan, ref context);
        context.Emitter.Writer.Append("return ");
        context.Visitor.Visit(node.Expression);
        context.Emitter.Writer.Append(';');
    }

    // <termination> return <constructed object>;
    private static void WriteConstructorSelf(ReturnPlan plan, ref TranspileContext context)
    {
        WriteStateTermination(plan, ref context);
        context.Emitter.Writer.Append($"return {GSharpInternalNames.Lowering.ConstructorSelf};");
    }

    // <termination> return;
    private static void WriteVoid(ReturnPlan plan, ref TranspileContext context)
    {
        WriteStateTermination(plan, ref context);
        context.Emitter.Writer.Append("return;");
    }

    private static void WriteStateTermination(ReturnPlan plan, ref TranspileContext context)
    {
        if (plan.EmitsStateTermination)
        {
            context.Emitter.Writer.Append(GSharpInternalNames.StateMachine.Termination);
        }
    }

    // A suspension reports "not completed"; a return of the user's code writes the result, resets the frame for the
    // next call and reports completion.
    private static void TranspileSuspendingDeviceFunctionReturn(
        ReturnStatementSyntax node,
        bool isStateMachineReturn,
        ref TranspileContext context
    )
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
