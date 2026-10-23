namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.Hint.Unroll</c> invocations into CUDA <c>#pragma unroll</c> directives preceding loop constructs.
/// </summary>
internal sealed class HintUnrollMacroRule : IMacroTranspilerRule
{
    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        GpuMacroCall call,
        ref TranspileContext context)
    {
        if (call.Kind != GpuMacroKind.HintUnroll)
        {
            return false;
        }

        if (GpuCodeConstraints.GetUnrolledLoop(statement) == null)
        {
            context.Reporter.Report(GSharpDiagnostics.UnrollHintMustPrecedeLoop, invocation);
            return true;
        }

        var args = invocation.ArgumentList.Arguments;
        var factor = 0;
        if (args.Count > 0)
        {
            var factorExpr = args[0].Expression;
            if (!GpuCodeConstraints.TryGetUnrollFactor(factorExpr, context.Query.SemanticModel, out factor))
            {
                context.Reporter.Report(GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant, factorExpr);
                return true;
            }
        }

        if (factor > 0)
        {
            context.Emitter.Writer.AppendLine($"#pragma unroll {factor}");
        }
        else
        {
            context.Emitter.Writer.AppendLine("#pragma unroll");
        }

        return true;
    }
}
