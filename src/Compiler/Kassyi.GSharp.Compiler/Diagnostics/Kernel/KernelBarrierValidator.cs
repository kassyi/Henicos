namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Encapsulates per-kernel AST scanning and validation state inside a zero-allocation ref struct.
/// Eliminates parameter bloat across validation steps while ensuring no heap allocations occur during analysis.
/// </summary>
/// <remarks>
/// <para>
/// A call to a device function that reaches a barrier suspends the caller like the barrier itself, so it counts as a
/// barrier for the cross-barrier rules. The divergence rule (GSH0037) counts only calls that reach a block barrier.
/// The same rules apply to the body of such a device function (<paramref name="isKernel"/> is false), which is lowered
/// into a state machine as well.
/// </para>
/// <para>
/// The stages live in partial files by responsibility: the constructs checked or collected node by node
/// (<c>.Constructs</c>), the divergence of a barrier (<c>.Divergence</c>), the rules about what crosses a barrier
/// (<c>.CrossBarrier</c>) and the shape of a device iterator (<c>.Iterator</c>). The <c>ISyntaxValidationRule&lt;T&gt;</c>
/// registry is not used for the prohibitions: it runs on kernels only, while this validator also runs on device
/// functions that suspend.
/// </para>
/// </remarks>
internal readonly ref partial struct KernelBarrierValidator(
    SyntaxNodeAnalysisContext context,
    MethodDeclarationSyntax methodDecl,
    SemanticModel semanticModel,
    bool isKernel)
{
    private readonly ImmutableArray<InvocationExpressionSyntax>.Builder _barriers =
        ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();

    private readonly ImmutableArray<GotoStatementSyntax>.Builder _gotos =
        ImmutableArray.CreateBuilder<GotoStatementSyntax>();

    private readonly ImmutableArray<LabeledStatementSyntax>.Builder _labels =
        ImmutableArray.CreateBuilder<LabeledStatementSyntax>();

    private readonly ImmutableArray<TrackedLocalInfo>.Builder _trackedLocals =
        ImmutableArray.CreateBuilder<TrackedLocalInfo>();

    private readonly ImmutableArray<LocalFunctionStatementSyntax>.Builder _localFunctionsWithBarrier =
        ImmutableArray.CreateBuilder<LocalFunctionStatementSyntax>();

    private readonly bool _isDeviceIterator = !isKernel && IteratorTypeResolver.ContainsDirectYield(methodDecl.Body);

    // Set by the scan when the body has a call that splits the state machine (a barrier, a warp shuffle, a grid sync, a
    // call that suspends); only then is the control flow graph built for the cross-barrier rules
    private readonly StrongBox<bool> _hasSplitPoint = new();

    // Computed on the first branch around a block barrier
    private readonly ThreadVarianceAnalysis _variance = new(semanticModel, methodDecl);

    public void Analyze()
    {
        // 1. The header: async (GSH0046) and the shape of a device iterator
        ValidateKernelHeader();
        if (methodDecl.Body == null && methodDecl.ExpressionBody == null)
        {
            return;
        }

        // 2. One pass over the body: the prohibitions reported on the spot, and what the later stages need
        ScanKernelBody();

        // 3. Locals live across a point where the state machine splits (GSH0039, GSH0041, GSH0072), on the control flow
        // graph: every split point counts, also a grid sync or a warp shuffle that the barrier list leaves out
        ValidateCrossBarrierLocals();
        if (_barriers.Count == 0)
        {
            return;
        }

        // 4. The rules about barriers in local functions (GSH0045) and jumps across them (GSH0040)
        ValidateRecursiveLocalFunctions();
        ValidateCrossBarrierGotos();
    }

    /// <summary>
    /// Visits every node of the body once: the syntax-only prohibitions are reported where they are found, and the
    /// barriers, jumps, labels and tracked locals are collected for the later stages.
    /// </summary>
    private void ScanKernelBody()
    {
        foreach (var node in methodDecl.DescendantNodes())
        {
            switch (node)
            {
                case LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax: CheckIteratorFunction(node); break;
                case AwaitExpressionSyntax or YieldStatementSyntax or LockStatementSyntax or TryStatementSyntax:
                    CheckProhibitedConstruct(node); break;
                case LocalDeclarationStatementSyntax declaration: CheckIteratorSharedMemory(declaration); break;
                // goto case and goto default jump within a switch, never to a label
                case GotoStatementSyntax jump when jump.IsKind(SyntaxKind.GotoStatement): _gotos.Add(jump); break;
                case LabeledStatementSyntax label: _labels.Add(label); break;
                case VariableDeclaratorSyntax declarator: InspectLocalVariableDeclarator(declarator); break;
                case InvocationExpressionSyntax invocation: InspectInvocation(invocation); break;
            }
        }
    }
}
