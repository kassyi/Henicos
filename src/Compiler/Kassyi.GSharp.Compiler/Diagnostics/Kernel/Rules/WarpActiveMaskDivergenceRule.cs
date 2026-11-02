namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports <c>Warp.ActiveMask()</c> under a condition that may differ between the threads (GSH0101, a Warning) on the CPU
/// dialects (#789). The CPU paths return every lane of the warp that exists in the block; the GPU returns only the lanes
/// that reach the call together, which under such a condition may be fewer.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] Both CPU paths run the threads of a warp one after another, so they cannot tell which lanes would share a path
/// on the GPU. Reported are the calls that <see cref="UniformityAnalysis.IsReachedByPartOfBlock"/> finds in the influence
/// region of a thread-variant branch, an earlier <c>return</c>, <c>break</c> or <c>continue</c> under such a condition
/// included (the bounds guard <c>if (t &gt;= n) return;</c>). The last round of <c>Gpu.GridStrideLoop</c> is not (a
/// suspicion is never reported, semantics charter §3). CUDA runs the call as written, so nothing is reported there.
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
        var masks = isKernel ? default : new ArgumentMasks(ulong.MaxValue, 0);
        if (new MethodFlowGraph(context.SemanticModel, declaration).TryGetUniformity(masks, out var uniformity) &&
            uniformity.IsReachedByPartOfBlock(invocation))
        {
            context.ReportDiagnostic(GSharpDiagnostics.WarpActiveMaskInDivergentCode, invocation,
                context.MethodSymbol.Name);
        }
    }
}
