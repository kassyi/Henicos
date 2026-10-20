namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.GridStrideLoop</c> macros into idiomatic grid-stride for-loops in CUDA C++.
/// </summary>
internal sealed class GridStrideLoopMacroRule : IMacroTranspilerRule
{
    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        IMethodSymbol symbol,
        ref TranspileContext context)
    {
        if (symbol.Name != TypeConstants.GpuMacros.GridStrideLoop || symbol.ContainingType.Name != TypeConstants.GpuType)
        {
            return false;
        }

        var hasContext = symbol.Parameters.Length == 3;
        var offset = hasContext ? 1 : 0;
        var args = invocation.ArgumentList.Arguments;

        if (args.Count < offset + 2 || args[offset + 1].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            return false;
        }

        if (!MacroTranspilerHelper.TryBeginMacroScope(
                invocation,
                lambdaExpr,
                hasContext,
                args,
                ref context,
                TypeConstants.GpuMacros.GridStrideLoop,
                out var macroScope))
        {
            return false;
        }

        using var scopeDisposer = macroScope;

        // TryBeginMacroScope has already rejected lambdas without a loop parameter.
        var loopVariable = MacroTranspilerHelper.ExtractUnrollLoopParameter(lambdaExpr, hasContext);
        if (loopVariable == null)
        {
            return false;
        }

        // Emit: for (int {paramName} = blockIdx.x * blockDim.x + threadIdx.x; {paramName} < [EXPR]; {paramName} += gridDim.x * blockDim.x)
        context.Emitter.Writer.AppendKeyword("for");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append("int ");
            CppNameResolver.AppendDeclaredName(loopVariable, loopVariable.Identifier, ref context);
            context.Emitter.Writer.Append(" = blockIdx.x * blockDim.x + threadIdx.x; ");
            CppNameResolver.AppendDeclaredName(loopVariable, loopVariable.Identifier, ref context);
            context.Emitter.Writer.Append(" < ");
            context.Visitor.Visit(args[offset].Expression);
            context.Emitter.Writer.Append("; ");
            CppNameResolver.AppendDeclaredName(loopVariable, loopVariable.Identifier, ref context);
            context.Emitter.Writer.Append(" += gridDim.x * blockDim.x");
        }

        context.Emitter.Writer.AppendLine();
        MacroTranspilerHelper.EmitUnrolledBody(ref context, lambdaExpr.Body);

        return true;
    }
}
