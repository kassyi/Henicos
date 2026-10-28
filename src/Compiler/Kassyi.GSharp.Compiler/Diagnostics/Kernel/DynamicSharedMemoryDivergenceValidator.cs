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
/// The branch and loop conditions are those of GSH0037 (<see cref="BarrierDivergenceChecks.GetDivergingCondition"/>),
/// plus the arms of a switch expression, decided by <see cref="ThreadVarianceAnalysis"/> within one method: the
/// parameters of the method are uniform, so a device function whose count comes from a thread-variant argument of its
/// caller is not reported.
/// </para>
/// </remarks>
internal static class DynamicSharedMemoryDivergenceValidator
{
    /// <summary>Reports GSH0077 in the body of the kernel or device function <paramref name="method"/>.</summary>
    public static void Analyze(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax method,
        SemanticModel semanticModel)
    {
        // Computed on the first dynamic declaration or call that reaches one
        ThreadVarianceAnalysis? variance = null;
        foreach (var node in method.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax { Parent.Parent: LocalDeclarationStatementSyntax local } declarator:
                    AnalyzeDeclarator(context, method, semanticModel, local, declarator, ref variance);
                    break;
                case InvocationExpressionSyntax invocation:
                    AnalyzeInvocation(context, method, semanticModel, invocation, ref variance);
                    break;
            }
        }
    }

    // A declaration under a thread-variant condition and with a thread-variant count is reported once for each
    private static void AnalyzeDeclarator(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax method,
        SemanticModel semanticModel, LocalDeclarationStatementSyntax local, VariableDeclaratorSyntax declarator,
        ref ThreadVarianceAnalysis? variance)
    {
        if (!semanticModel.GetTypeSymbol(local.Declaration.Type).IsSharedMemoryType() ||
            !DynamicSharedMemoryAnalysis.TryGetCount(declarator, semanticModel, out var creation, out var count,
                out var constantCount) ||
            constantCount != null)
        {
            return;
        }

        variance ??= new ThreadVarianceAnalysis(semanticModel, method);
        var name = declarator.Identifier.ValueText;
        if (IsReachedByPartOfBlock(creation, method, variance))
        {
            context.ReportDiagnostic(GSharpDiagnostics.DivergentDynamicSharedMemory, creation,
                $"Only some threads of the block reach the dynamic SharedMemory<T> declaration '{name}'");
        }

        if (variance.IsThreadVariant(count))
        {
            context.ReportDiagnostic(GSharpDiagnostics.DivergentDynamicSharedMemory, count,
                $"The count of the dynamic SharedMemory<T> declaration '{name}' depends on the thread");
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax method,
        SemanticModel semanticModel, InvocationExpressionSyntax invocation, ref ThreadVarianceAnalysis? variance)
    {
        if (semanticModel.GetSymbol(invocation) is not IMethodSymbol callee ||
            !DynamicSharedMemoryAnalysis.ReachesDynamicSharedMemory(callee, semanticModel.Compilation))
        {
            return;
        }

        variance ??= new ThreadVarianceAnalysis(semanticModel, method);
        if (IsReachedByPartOfBlock(invocation, method, variance))
        {
            context.ReportDiagnostic(GSharpDiagnostics.DivergentDynamicSharedMemory, invocation,
                $"Only some threads of the block reach the call to '{callee.Name}', which reaches a dynamic " +
                "SharedMemory<T> declaration");
        }
    }

    // Whether a branch, loop, operand or switch expression arm with a thread-variant condition encloses the node;
    // reported once however many do
    private static bool IsReachedByPartOfBlock(SyntaxNode node, MethodDeclarationSyntax method,
        ThreadVarianceAnalysis variance)
    {
        var child = node;
        for (var ancestor = node.Parent;
             ancestor != null && ancestor != method;
             child = ancestor, ancestor = ancestor.Parent)
        {
            if (GetDivergingCondition(ancestor, child) is { } condition && variance.IsThreadVariant(condition))
            {
                return true;
            }
        }

        return false;
    }

    // [WHY] GetDivergingCondition leaves out switch expression arms because a barrier there is GSH0062, not GSH0037;
    // a dynamic declaration reached from an arm has no other diagnostic, so the arms are added here
    private static ExpressionSyntax? GetDivergingCondition(SyntaxNode ancestor, SyntaxNode child) =>
        BarrierDivergenceChecks.GetDivergingCondition(ancestor, child) ??
        (ancestor is SwitchExpressionSyntax switchExpression && child != switchExpression.GoverningExpression
            ? switchExpression.GoverningExpression
            : null);
}
