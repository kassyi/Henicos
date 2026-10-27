namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.ForUnroll</c> macro loops by expanding them at compile-time and substituting the induction variable with constant literals.
/// </summary>
internal sealed class ForUnrollMacroRule : IMacroTranspilerRule
{
    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        GpuMacroCall call,
        ref TranspileContext context)
    {
        if (call.Kind != GpuMacroKind.ForUnroll)
        {
            return false;
        }

        var offset = call.ArgumentOffset;
        var args = invocation.ArgumentList.Arguments;

        if (args.Count <= call.LambdaArgumentIndex || args[call.LambdaArgumentIndex].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            return false;
        }

        var hasStart = GpuCodeConstraints.TryGetMacroConstantInt(args[offset].Expression, context.Query.SemanticModel,
            out var start);
        var hasCount = GpuCodeConstraints.TryGetMacroConstantInt(args[offset + 1].Expression, context.Query.SemanticModel,
            out var count);
        if (!hasStart || !hasCount)
        {
            FailIfNotConstant(hasStart, args[offset].Expression, "start", ref context);
            FailIfNotConstant(hasCount, args[offset + 1].Expression, "count", ref context);
            return false;
        }

        if (!MacroTranspilerHelper.TryBeginMacroScope(invocation, lambdaExpr, call.HasContext, args, ref context, TypeConstants.GpuMacros.ForUnroll, out var macroScope))
        {
            return false;
        }

        using var scopeDisposer = macroScope;

        context.Emitter.Writer.AppendLine($"// [GpuUnroll] start={start}, count={count}");

        // [WHY] Unrolls the loop at C# source transpilation time by substituting the loop induction variable with literal constants, eliminating runtime branching in the GPU kernel.
        var intType = context.Query.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        for (var i = start; i < start + count; i++)
        {
            // A negative index is parenthesized ((-2)), and its labels spell the sign as a letter (_Unroll_m2)
            var iterationCookie = context.Emitter.PushOverrideScope(
                macroScope.ParamName,
                new OverrideValue(ConstexprLiteral.FormatCppConstant(intType, i))
            );
            var oldSuffix = context.Emitter.LabelSuffix;
            context.Emitter.LabelSuffix = i < 0
                ? "_Unroll_m" + (-(long)i).ToString(CultureInfo.InvariantCulture)
                : "_Unroll_" + i.ToString(CultureInfo.InvariantCulture);
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

    private static void FailIfNotConstant(bool isConstant, ExpressionSyntax argument, string parameterName,
        ref TranspileContext context)
    {
        if (!isConstant)
        {
            context.Reporter.Fail(GSharpDiagnostics.MacroArgumentMustBeConstant, argument, parameterName,
                TypeConstants.GpuMacros.ForUnroll);
        }
    }
}
