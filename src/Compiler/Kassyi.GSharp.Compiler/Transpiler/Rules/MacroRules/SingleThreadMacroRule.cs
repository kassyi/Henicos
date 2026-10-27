namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.SingleThread</c> blocks by wrapping them in the guard of <see cref="SingleThreadGuard"/>: only the
/// thread whose six indices are all 0 runs the body.
/// </summary>
/// <remarks>
/// A <c>return</c> of the body leaves the body, as in C# (#693): when the body has one (not in a lambda or local function
/// of the body), the label <c>__gsharp_st_end_&lt;n&gt;</c> follows the guard, and each such <c>return</c> becomes
/// <c>goto __gsharp_st_end_&lt;n&gt;;</c> (<see cref="StatementTranspilers.TranspileReturnStatement"/>).
/// </remarks>
internal sealed class SingleThreadMacroRule : IMacroTranspilerRule
{
    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        GpuMacroCall call,
        ref TranspileContext context)
    {
        if (call.Kind != GpuMacroKind.SingleThread)
        {
            return false;
        }

        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 1 || args[0].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            return false;
        }

        context.Emitter.Writer.Append("if");
        using (context.Emitter.Writer.Parentheses())
        {
            SingleThreadGuard.AppendCppCondition(context.Emitter.Writer);
        }

        context.Emitter.Writer.AppendLine();

        string? endLabel = SyncGridBody.ContainsBodyReturn(lambdaExpr.Body)
            ? $"{GSharpInternalNames.Lowering.SingleThreadEndLabelPrefix}{context.Emitter.SingleThreadBodyCounter++}"
            : null;
        var outerLabel = context.Emitter.SingleThreadEndLabel;
        var outerDepth = context.Emitter.SingleThreadCallableDepth;
        context.Emitter.SingleThreadEndLabel = endLabel;
        context.Emitter.SingleThreadCallableDepth = context.Emitter.CallableDepth;
        try
        {
            MacroTranspilerHelper.EmitUnrolledBody(ref context, lambdaExpr.Body);
        }
        finally
        {
            context.Emitter.SingleThreadEndLabel = outerLabel;
            context.Emitter.SingleThreadCallableDepth = outerDepth;
        }

        context.Emitter.Writer.AppendLineIf(endLabel != null, $"{endLabel}:;");
        return true;
    }
}
