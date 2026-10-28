namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// The state of one run of <see cref="KernelBarrierValidator"/>: the method and its analysis context, and what the scan
/// of its body collects for the later checks.
/// </summary>
internal sealed class KernelBarrierScan(
    SyntaxNodeAnalysisContext context,
    MethodDeclarationSyntax method,
    SemanticModel semanticModel,
    bool isKernel)
{
    public SyntaxNodeAnalysisContext Context { get; } = context;

    /// <summary>The kernel, or the device function that may suspend.</summary>
    public MethodDeclarationSyntax Method { get; } = method;

    public SemanticModel SemanticModel { get; } = semanticModel;

    /// <summary>Whether <see cref="Method"/> is a kernel rather than a device function.</summary>
    public bool IsKernel { get; } = isKernel;

    /// <summary>Whether <see cref="Method"/> is a device function that yields (a device iterator).</summary>
    public bool IsDeviceIterator { get; } = !isKernel && IteratorTypeResolver.ContainsDirectYield(method.Body);

    /// <summary>The block barriers and the calls that suspend, in text order.</summary>
    public ImmutableArray<InvocationExpressionSyntax>.Builder Barriers { get; } =
        ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();

    public ImmutableArray<GotoStatementSyntax>.Builder Gotos { get; } = ImmutableArray.CreateBuilder<GotoStatementSyntax>();

    public ImmutableArray<LabeledStatementSyntax>.Builder Labels { get; } =
        ImmutableArray.CreateBuilder<LabeledStatementSyntax>();

    /// <summary>The locals that must not be live across a split point.</summary>
    public ImmutableArray<TrackedLocalInfo>.Builder TrackedLocals { get; } =
        ImmutableArray.CreateBuilder<TrackedLocalInfo>();

    public ImmutableArray<LocalFunctionStatementSyntax>.Builder LocalFunctionsWithBarrier { get; } =
        ImmutableArray.CreateBuilder<LocalFunctionStatementSyntax>();

    /// <summary>
    /// Whether the body has a call that splits the state machine (a barrier, a warp shuffle, a grid sync, a call that
    /// suspends); only then is the control flow graph built for the cross-barrier rules.
    /// </summary>
    public bool HasSplitPoint { get; set; }

    /// <summary>Computed on the first branch around a block barrier.</summary>
    public ThreadVarianceAnalysis Variance { get; } = new(semanticModel, method);
}
