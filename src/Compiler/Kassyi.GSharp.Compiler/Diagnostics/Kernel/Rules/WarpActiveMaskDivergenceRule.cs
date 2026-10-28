namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports <c>Warp.ActiveMask()</c> under a condition that may differ between the threads (GSH0101, a Warning) on the CPU
/// dialects (#789). The CPU paths return every lane of the warp that exists in the block; the GPU returns only the lanes
/// that reach the call together, which under such a condition may be fewer.
/// </summary>
/// <remarks>
/// [WHY] Both CPU paths run the threads of a warp one after another, so they cannot tell which lanes would share a path
/// on the GPU. Only the conditions that <see cref="ThreadVarianceAnalysis.IsReachedUnderVariantCondition"/> sees are
/// reported: an earlier thread-dependent <c>return</c> or <c>break</c> and the last round of <c>Gpu.GridStrideLoop</c> are
/// not (a suspicion is never reported, semantics charter §3). CUDA runs the call as written, so nothing is reported there.
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

        var variance = new ThreadVarianceAnalysis(context.SemanticModel, declaration);
        if (variance.IsReachedUnderVariantCondition(invocation))
        {
            context.ReportDiagnostic(GSharpDiagnostics.WarpActiveMaskInDivergentCode, invocation,
                context.MethodSymbol.Name);
        }
    }
}
