namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Finds the <c>goto</c>s to the inner label of chained labels (<c>second</c> of <c>first: second: s</c>) from another
/// state of the CPU state machine, which the generator reports as GSH0121 (<see cref="HeldRefLocalSite"/>).
/// </summary>
/// <remarks>
/// [WHY] Whether the goto and the label share a state is decided on the flattened body: lowering detaches some labels
/// (the labels of a statement that holds a split point, the first labels of a reconvergence region) and starts states
/// at others. The generator reports the list that its own lowering made
/// (<see cref="LoweredStateMachine.GotosToChainedLabelsAcrossStates"/>), on a project that builds the CPU paths and for
/// a device function of one that exports its device code (a CUDA kernel runs as written).
/// GSH0121 is the generator's alone: the analyzer does not report it. An IDE shows the diagnostics of the analyzer and
/// of the generator side by side and does not merge equal ones at the same place, so a diagnostic reported from both
/// would be seen twice (#2121). A build without the analyzers still stops, and a method that the generator cannot lower
/// is reported as GSH0048.
/// </remarks>
internal static class ChainedLabelJumpChecks
{
    /// <summary>
    /// The gotos of <paramref name="lowered"/> that cannot be lowered
    /// (<see cref="LoweredStateMachine.GotosToChainedLabelsAcrossStates"/>), each with its place in the user's source and
    /// whether it crosses a barrier or a call that suspends: such a goto is GSH0040's
    /// (<see cref="CrossBarrierChecks.IsCrossBarrierGoto"/>), every other one GSH0121's.
    /// </summary>
    /// <remarks>
    /// The one definition of the gotos the generator reports; it reports GSH0040 too for the gotos across a barrier, as
    /// the analyzer does (<see cref="CrossBarrierChecks"/>), since a build without the analyzers would otherwise pass
    /// with the managed body left out. The goto of the
    /// lowered body is found again in the user's source by its reported place, and its label as GSH0040 finds it
    /// (<see cref="CrossBarrierChecks.FindTargetLabel"/>). The barriers are the calls that run (<see cref="RemovedCalls"/>) and
    /// <see cref="KernelConstructChecks.IsBarrierCall"/> accepts; in a device iterator, where the scan collects none
    /// and reports each split point as GSH0072, a goto across one is left to that error.
    /// </remarks>
    internal static ImmutableArray<(Location Location, ChainedLabelGoto Jump, bool CrossesBarrier)> FindReported(
        MethodDeclarationSyntax method, SemanticModel semanticModel, LoweredStateMachine lowered)
    {
        if (lowered.GotosToChainedLabelsAcrossStates.IsEmpty)
        {
            return ImmutableArray<(Location, ChainedLabelGoto, bool)>.Empty;
        }

        ImmutableArray<InvocationExpressionSyntax>.Builder? barriers = null;
        ImmutableArray<LabeledStatementSyntax>.Builder? labels = null;
        var reported = ImmutableArray.CreateBuilder<(Location, ChainedLabelGoto, bool)>();
        foreach (var jump in lowered.GotosToChainedLabelsAcrossStates)
        {
            var location = semanticModel.GetReportLocation(jump.Goto);
            var crossesBarrier = false;
            // The goto of the compilation, beside the barriers and labels of <method>: for a closed copy of a generic
            // declaration the report location is in the user's tree, not the copy's
            if (semanticModel.GetCompilationNode(jump.Goto)?.FirstAncestorOrSelf<GotoStatementSyntax>() is
                { } sourceGoto)
            {
                if (barriers is null || labels is null)
                {
                    (barriers, labels) = CollectBarriersAndLabels(method, semanticModel);
                }

                crossesBarrier =
                    CrossBarrierChecks.FindTargetLabel(labels, sourceGoto, semanticModel) is { } target &&
                    CrossBarrierChecks.IsCrossBarrierGoto(barriers, sourceGoto, target);
            }

            reported.Add((location, jump, crossesBarrier));
        }

        return reported.ToImmutable();
    }

    // The barriers of GSH0040 and the labels of the method, in text order, as KernelBarrierValidator collects them
    private static (ImmutableArray<InvocationExpressionSyntax>.Builder Barriers, ImmutableArray<LabeledStatementSyntax>.Builder Labels)
        CollectBarriersAndLabels(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        var barriers = ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();
        var labels = ImmutableArray.CreateBuilder<LabeledStatementSyntax>();
        foreach (var node in RemovedCalls.DescendantNodesThatRun(method, semanticModel))
        {
            switch (node)
            {
                case LabeledStatementSyntax label:
                    labels.Add(label);
                    break;
                case InvocationExpressionSyntax invocation
                    when semanticModel.GetSymbol(invocation) is IMethodSymbol callee &&
                         KernelConstructChecks.IsBarrierCall(callee, semanticModel.Compilation):
                    barriers.Add(invocation);
                    break;
            }
        }

        return (barriers, labels);
    }
}
