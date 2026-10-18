
namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.Hint.Unroll</c> invocations into CUDA <c>#pragma unroll</c> directives preceding loop constructs.
/// </summary>
internal sealed class HintUnrollMacroRule : IMacroTranspilerRule
{
    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        IMethodSymbol symbol,
        ref TranspileContext context)
    {
        if (symbol.Name != TypeConstants.GpuMacros.Unroll ||
            symbol.ContainingType.Name != TypeConstants.GpuMacros.Hint ||
            symbol.ContainingType.ContainingType?.Name != TypeConstants.GpuType)
        {
            return false;
        }

        if (GpuMacroArgumentRule.GetSupportedLoop(statement) == null)
        {
            context.Reporter.Report(GSharpDiagnostics.UnrollHintMustPrecedeLoop, invocation);
            return true;
        }

        var args = invocation.ArgumentList.Arguments;
        var factor = 0;
        if (args.Count > 0)
        {
            var factorExpr = args[0].Expression;
            if (!MacroTranspilerHelper.TryGetConstantInt(factorExpr, context.Query.SemanticModel, out factor) || factor < 0)
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
