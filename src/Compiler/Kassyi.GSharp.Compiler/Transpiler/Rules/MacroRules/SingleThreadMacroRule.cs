namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.SingleThread</c> blocks by wrapping them in a thread 0 / block 0 conditional execution guard.
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
            context.Emitter.Writer.Append("threadIdx.x == 0 && blockIdx.x == 0");
        }

        context.Emitter.Writer.AppendLine();

        MacroTranspilerHelper.EmitUnrolledBody(ref context, lambdaExpr.Body);

        return true;
    }
}
