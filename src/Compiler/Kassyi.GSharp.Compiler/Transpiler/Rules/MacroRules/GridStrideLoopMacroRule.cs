namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.GridStrideLoop</c> macros into idiomatic grid-stride for-loops in CUDA C++.
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
        var loopVariable = MacroTranspilerHelper.ExtractUnrollLoopParameter(lambdaExpr, call.HasContext);
        if (loopVariable == null)
        {
            return false;
        }

        // Emit: for (int {paramName} = blockIdx.x * blockDim.x + threadIdx.x; {paramName} < [EXPR]; {paramName} += gridDim.x * blockDim.x)
        context.Emitter.Writer.Append("for");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append("int ");
            CppNameResolver.AppendDeclaredName(loopVariable, loopVariable.Identifier, ref context);
            context.Emitter.Writer.Append(" = blockIdx.x * blockDim.x + threadIdx.x; ");
            CppNameResolver.AppendDeclaredName(loopVariable, loopVariable.Identifier, ref context);
            context.Emitter.Writer.Append(" < ");
            context.Visitor.Visit(args[call.ArgumentOffset].Expression);
            context.Emitter.Writer.Append("; ");
            CppNameResolver.AppendDeclaredName(loopVariable, loopVariable.Identifier, ref context);
            context.Emitter.Writer.Append(" += gridDim.x * blockDim.x");
        }

        context.Emitter.Writer.AppendLine();
        MacroTranspilerHelper.EmitUnrolledBody(ref context, lambdaExpr.Body);

        return true;
    }
}
