namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.SingleThread</c> blocks by wrapping them in the guard of <see cref="SingleThreadGuard"/>: only the
/// thread whose six indices are all 0 runs the body.
/// </summary>
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

        MacroTranspilerHelper.EmitUnrolledBody(ref context, lambdaExpr.Body);

        return true;
    }
}
