namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Validates the barriers of a kernel or of a device function that may suspend: the facade over the checks, which share
/// the state of one run (<see cref="KernelBarrierScan"/>).
/// </summary>
/// <remarks>
/// <para>
/// A call to a device function that reaches a barrier suspends the caller like the barrier itself, so it counts as a
/// barrier for the cross-barrier rules. The divergence rule (GSH0037) counts only calls that reach a block barrier.
/// The same rules apply to the body of such a device function (<paramref name="isKernel"/> is false), which is lowered
/// into a state machine as well.
/// </para>
/// <para>
/// The checks are classes by responsibility: the constructs checked or collected node by node
/// (<see cref="KernelConstructChecks"/>), where a barrier stands (<see cref="BarrierDivergenceChecks"/>), what must not
/// cross a barrier (<see cref="CrossBarrierChecks"/>) and the shape of a device iterator (<see cref="DeviceIteratorChecks"/>).
/// The <c>ISyntaxValidationRule&lt;T&gt;</c> registry is not used for the prohibitions: it runs on kernels only, while
/// this validator also runs on device functions that suspend.
/// </para>
/// </remarks>
internal readonly ref struct KernelBarrierValidator(
    SyntaxNodeAnalysisContext context,
    MethodDeclarationSyntax methodDecl,
    SemanticModel semanticModel,
    bool isKernel)
{
    private readonly KernelBarrierScan _scan = new(context, methodDecl, semanticModel, isKernel);

    public void Analyze()
    {
        // 1. The header: async (GSH0046) and the shape of a device iterator
        KernelConstructChecks.ValidateKernelHeader(_scan);
        if (methodDecl.Body == null && methodDecl.ExpressionBody == null)
        {
            return;
        }

        // 2. One pass over the body: the prohibitions reported on the spot, and what the later checks need
        ScanKernelBody();

        // 3. Locals live across a point where the state machine splits (GSH0039, GSH0041, GSH0072), on the control flow
        // graph: every split point counts, also a grid sync or a warp shuffle that the barrier list leaves out
        CrossBarrierChecks.ValidateCrossBarrierLocals(_scan);
        if (_scan.Barriers.Count == 0)
        {
            return;
        }

        // 4. The rules about barriers in local functions (GSH0045) and jumps across them (GSH0040)
        CrossBarrierChecks.ValidateRecursiveLocalFunctions(_scan);
        CrossBarrierChecks.ValidateCrossBarrierGotos(_scan);
    }

    /// <summary>
    /// Visits every node of the body once: the syntax-only prohibitions are reported where they are found, and the
    /// barriers, jumps, labels and tracked locals are collected for the later checks.
    /// </summary>
    private void ScanKernelBody()
    {
        foreach (var node in methodDecl.DescendantNodes())
        {
            switch (node)
            {
                case LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax:
                    KernelConstructChecks.CheckIteratorFunction(_scan, node); break;
                case AwaitExpressionSyntax or YieldStatementSyntax or LockStatementSyntax or TryStatementSyntax:
                    KernelConstructChecks.CheckProhibitedConstruct(_scan, node); break;
                case LocalDeclarationStatementSyntax declaration:
                    KernelConstructChecks.CheckIteratorSharedMemory(_scan, declaration); break;
                // goto case and goto default jump within a switch, never to a label
                case GotoStatementSyntax jump when jump.IsKind(SyntaxKind.GotoStatement): _scan.Gotos.Add(jump); break;
                case LabeledStatementSyntax label: _scan.Labels.Add(label); break;
                case VariableDeclaratorSyntax declarator:
                    KernelConstructChecks.InspectLocalVariableDeclarator(_scan, declarator); break;
                case InvocationExpressionSyntax invocation: KernelConstructChecks.InspectInvocation(_scan, invocation); break;
            }
        }
    }
}
