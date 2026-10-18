namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.ForUnroll</c> macro loops by expanding them at compile-time and substituting the induction variable with constant literals.
/// </summary>
internal sealed class ForUnrollMacroRule : IMacroTranspilerRule
{
    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        IMethodSymbol symbol,
        ref TranspileContext context)
    {
        if (symbol.Name != TypeConstants.GpuMacros.ForUnroll || symbol.ContainingType.Name != TypeConstants.GpuType)
        {
            return false;
        }

        var hasContext = symbol.Parameters.Length == 4;
        var offset = hasContext ? 1 : 0;
        var args = invocation.ArgumentList.Arguments;

        if (args.Count < offset + 3 || args[offset + 2].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            return false;
        }

        if (!MacroTranspilerHelper.TryGetConstantInt(args[offset].Expression, context.Query.SemanticModel, out var start) ||
            !MacroTranspilerHelper.TryGetConstantInt(args[offset + 1].Expression, context.Query.SemanticModel, out var count))
        {
            context.Reporter.Report(GSharpDiagnostics.UnsupportedSyntax, invocation, "ForUnroll arguments must be constants");
            return false;
        }

        if (!MacroTranspilerHelper.TryBeginMacroScope(invocation, lambdaExpr, hasContext, args, ref context, TypeConstants.GpuMacros.ForUnroll, out var macroScope))
        {
            return false;
        }

        using var scopeDisposer = macroScope;

        context.Emitter.Writer.AppendLine($"// [GpuUnroll] start={start}, count={count}");

        // [WHY] Unrolls the loop at C# source transpilation time by substituting the loop induction variable with literal constants, eliminating runtime branching in the GPU kernel.
        for (var i = start; i < start + count; i++)
        {
            var iterationCookie = context.Emitter.PushOverrideScope(macroScope.ParamName, new OverrideValue(i.ToString()));
            var oldSuffix = context.Emitter.LabelSuffix;
            context.Emitter.LabelSuffix = $"_Unroll_{i}";
            try
            {
                MacroTranspilerHelper.EmitUnrolledBody(ref context, lambdaExpr.Body);
            }
            finally
            {
                context.Emitter.LabelSuffix = oldSuffix;
                context.Emitter.PopOverrideScope(iterationCookie);
            }
        }

        return true;
    }
}
