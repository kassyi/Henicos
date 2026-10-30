namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Warns about a dynamic <c>SharedMemory&lt;T&gt;</c> declaration, or a call to a device function that reaches one,
/// that only some threads of a block reach, and about a dynamic declaration whose count depends on the thread
/// (GSH0077).
/// </summary>
/// <remarks>
/// <para>
/// [WHY] On CUDA each thread keeps its own table of the dynamic regions of its block and places a region where its own
/// cursor is when it first reaches the declaration, with the count it computes (#439). Threads that reach the
/// declarations in different orders, or with different counts, put the same region at different offsets and read each
/// other's data. The CPU dialects and managed execution keep one table per block, filled by the first thread to arrive,
/// so the same kernel may also give different results in different dialects. It is a warning: when no other dynamic
/// declaration is placed after the divergent one, every thread still agrees.
/// </para>
/// <para>
/// Both questions are answered on the control flow graph by <see cref="UniformityAnalysis"/>, within one method: a
/// declaration or call is reached by only some threads when it lies in the influence region of a branch that varies
/// within the block (an <c>if</c>, a loop, a <c>switch</c> statement or expression, a <c>?:</c>, the right operand of
/// <c>&amp;&amp;</c>, an early <c>return</c>), and the count is the value that reaches the declaration. The parameters
/// of the method are uniform, so a device function whose count comes from a thread-variant argument of its caller is
/// not reported. A body that does not bind (being edited) is not analyzed.
/// </para>
/// </remarks>
internal static class DynamicSharedMemoryDivergenceValidator
{
    /// <summary>Reports GSH0077 in the body of the kernel or device function <paramref name="method"/>.</summary>
    public static void Analyze(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax method,
        SemanticModel semanticModel, MethodFlowGraph flow)
    {
        foreach (var node in method.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax { Parent.Parent: LocalDeclarationStatementSyntax local } declarator:
                    AnalyzeDeclarator(context, semanticModel, local, declarator, flow);
                    break;
                case InvocationExpressionSyntax invocation:
                    AnalyzeInvocation(context, semanticModel, invocation, flow);
                    break;
            }
        }
    }

    // A declaration under a thread-variant condition and with a thread-variant count is reported once for each
    private static void AnalyzeDeclarator(SyntaxNodeAnalysisContext context, SemanticModel semanticModel,
        LocalDeclarationStatementSyntax local, VariableDeclaratorSyntax declarator, MethodFlowGraph flow)
    {
        if (!semanticModel.GetTypeSymbol(local.Declaration.Type).IsSharedMemoryType() ||
            !DynamicSharedMemoryAnalysis.TryGetCount(declarator, semanticModel, out var creation, out var count,
                out var constantCount) ||
            constantCount != null ||
            !flow.TryGetUniformity(out var uniformity))
        {
            return;
        }

        var name = declarator.Identifier.ValueText;
        if (uniformity.IsReachedByPartOfBlock(creation))
        {
            context.ReportDiagnostic(GSharpDiagnostics.DivergentDynamicSharedMemory, creation,
                $"Only some threads of the block reach the dynamic SharedMemory<T> declaration '{name}'");
        }

        if (uniformity.IsThreadVariant(count))
        {
            context.ReportDiagnostic(GSharpDiagnostics.DivergentDynamicSharedMemory, count,
                $"The count of the dynamic SharedMemory<T> declaration '{name}' depends on the thread");
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, SemanticModel semanticModel,
        InvocationExpressionSyntax invocation, MethodFlowGraph flow)
    {
        if (semanticModel.GetSymbol(invocation) is not IMethodSymbol callee ||
            !DynamicSharedMemoryAnalysis.ReachesDynamicSharedMemory(callee, semanticModel.Compilation) ||
            !flow.TryGetUniformity(out var uniformity))
        {
            return;
        }

        if (uniformity.IsReachedByPartOfBlock(invocation))
        {
            context.ReportDiagnostic(GSharpDiagnostics.DivergentDynamicSharedMemory, invocation,
                $"Only some threads of the block reach the call to '{callee.Name}', which reaches a dynamic " +
                "SharedMemory<T> declaration");
        }
    }
}
