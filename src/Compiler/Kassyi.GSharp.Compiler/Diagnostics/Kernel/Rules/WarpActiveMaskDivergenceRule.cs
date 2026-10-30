namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports <c>Warp.ActiveMask()</c> under a condition that may differ between the threads (GSH0101, a Warning) on the CPU
/// dialects (#789). The CPU paths return every lane of the warp that exists in the block; the GPU returns only the lanes
/// that reach the call together, which under such a condition may be fewer.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] Both CPU paths run the threads of a warp one after another, so they cannot tell which lanes would share a path
/// on the GPU. Reported are the conditions that <see cref="ThreadVarianceAnalysis.IsReachedUnderVariantCondition"/> sees,
/// and an earlier <c>return</c>, <c>break</c> or <c>continue</c> under such a condition that leaves the code of the call
/// (the bounds guard <c>if (t &gt;= n) return;</c>). The last round of <c>Gpu.GridStrideLoop</c> is not (a suspicion is
/// never reported, semantics charter §3). CUDA runs the call as written, so nothing is reported there.
/// </para>
/// <para>
/// A device function is analyzed with every parameter variant: a caller may pass each thread its own value.
/// </para>
/// </remarks>
internal sealed class WarpActiveMaskDivergenceRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly WarpActiveMaskDivergenceRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.WarpActiveMaskInDivergentCode);

    private WarpActiveMaskDivergenceRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax invocation, ref ValidationContext context)
    {
        if (context.Dialect.IsCuda ||
            context.SemanticModel.GetSymbol(invocation) is not IMethodSymbol method ||
            !SyncIntrinsicClassifier.IsWarpActiveMask(method) ||
            invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } declaration)
        {
            return;
        }

        var isKernel = context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(declaration) is { } declared &&
                       declared.HasAttribute(KnownAttributes.GpuKernel);
        var variance = new ThreadVarianceAnalysis(context.SemanticModel, declaration,
            variantParameters: isKernel ? 0 : ulong.MaxValue);
        if (variance.IsReachedUnderVariantCondition(invocation) || FollowsVariantExit(invocation, variance))
        {
            context.ReportDiagnostic(GSharpDiagnostics.WarpActiveMaskInDivergentCode, invocation,
                context.MethodSymbol.Name);
        }
    }

    // Whether a statement before the one that holds <invocation>, in a block around it, holds a return, break or continue
    // under a thread-dependent condition that leaves the code of the call: a return of the method, or a break or
    // continue of a loop (or switch) around the call. The threads that took it do not reach the call
    private static bool FollowsVariantExit(InvocationExpressionSyntax invocation, ThreadVarianceAnalysis variance)
    {
        for (SyntaxNode node = invocation; node.Parent is { } parent; node = parent)
        {
            if (parent is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax)
            {
                return false;
            }

            var statements = parent switch
            {
                BlockSyntax block => block.Statements,
                SwitchSectionSyntax section => section.Statements,
                _ => default
            };
            foreach (var statement in statements)
            {
                if (statement == node)
                {
                    break;
                }

                if (statement.DescendantNodesAndSelf(static n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                    .AsStackEnumerable()
                    .Any((Invocation: invocation, Variance: variance), static (n, s) =>
                        LeavesTheCall(n, s.Invocation) && s.Variance.IsReachedUnderVariantCondition(n)
                    ))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // A return, or a break or continue whose loop or switch holds <invocation>
    private static bool LeavesTheCall(SyntaxNode node, InvocationExpressionSyntax invocation) => node switch
    {
        ReturnStatementSyntax => true,
        BreakStatementSyntax or ContinueStatementSyntax =>
            GetJumpTarget(node) is { } target && target.Span.Contains(invocation.Span),
        _ => false
    };

    // The loop that a break or continue leaves, or the switch that a break leaves
    private static SyntaxNode? GetJumpTarget(SyntaxNode jump)
    {
        for (var node = jump.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax:
                    return node;
                case SwitchStatementSyntax when jump is BreakStatementSyntax:
                    return node;
                case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax:
                    return null;
            }
        }

        return null;
    }
}
