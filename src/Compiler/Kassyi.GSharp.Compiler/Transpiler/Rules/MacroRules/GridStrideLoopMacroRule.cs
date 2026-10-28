namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.GridStrideLoop</c> macros into grid-stride for-loops over the linear rank of the whole launch
/// (the Prelude helpers <c>__gsharp_grid_stride_start</c> / <c>_step</c> / <c>_count</c>), for CUDA and the native CPU.
/// </summary>
internal sealed class GridStrideLoopMacroRule : IMacroTranspilerRule
{
    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        GpuMacroCall call,
        ref TranspileContext context)
    {
        if (call.Kind != GpuMacroKind.GridStrideLoop)
        {
            return false;
        }

        var args = invocation.ArgumentList.Arguments;

        if (args.Count <= call.LambdaArgumentIndex || args[call.LambdaArgumentIndex].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            return false;
        }

        if (!MacroTranspilerHelper.TryBeginMacroScope(
                invocation,
                lambdaExpr,
                call.HasContext,
                args,
                ref context,
                TypeConstants.GpuMacros.GridStrideLoop,
                out var macroScope))
        {
            return false;
        }

        using var scopeDisposer = macroScope;

        // TryBeginMacroScope has already rejected lambdas without a loop parameter.
        var loopVariable = GpuCodeConstraints.GetMacroLoopParameter(lambdaExpr, call.HasContext);
        if (loopVariable == null)
        {
            return false;
        }

        // {
        // unsigned int __gsharp_gsl_n_<n> = __gsharp_grid_stride_count(<count>);
        // for(unsigned int __gsharp_gsl_<n> = __gsharp_grid_stride_start(); __gsharp_gsl_<n> < __gsharp_gsl_n_<n>; __gsharp_gsl_<n> += __gsharp_grid_stride_step())
        // {
        // int <i> = (int)__gsharp_gsl_<n>;
        // <body>
        // }
        // }
        // The count is read once, before the loop, as a C# argument is. The index is unsigned and the start and step
        // saturate at 2^31, so index + step never wraps around (#712)
        var number = context.Emitter.GridStrideLoopCounter++;
        var index = $"{GSharpInternalNames.Lowering.GridStrideIndexPrefix}{number}";
        var count = $"{GSharpInternalNames.Lowering.GridStrideCountPrefix}{number}";
        var writer = context.Emitter.Writer;
        using (writer.Scope())
        {
            writer.Append($"unsigned int {count} = {GSharpInternalNames.Prelude.GridStrideCount}");
            using (writer.Parentheses())
            {
                context.Visitor.Visit(args[call.ArgumentOffset].Expression);
            }

            writer.AppendLine(";");
            writer.AppendLine(
                $"for(unsigned int {index} = {GSharpInternalNames.Prelude.GridStrideStart}(); {index} < {count}; {index} += {GSharpInternalNames.Prelude.GridStrideStep}())"
            );
            using (writer.Scope())
            {
                writer.Append("int ");
                CppNameResolver.AppendDeclaredName(loopVariable, loopVariable.Identifier, ref context);
                writer.AppendLine($" = (int){index};");
                MacroTranspilerHelper.EmitUnrolledBody(ref context, lambdaExpr.Body);
            }
        }

        return true;
    }
}
