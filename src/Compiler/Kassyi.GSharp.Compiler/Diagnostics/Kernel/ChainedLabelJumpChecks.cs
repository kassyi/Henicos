namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Reports a <c>goto</c> to the inner label of chained labels (<c>second</c> of <c>first: second: s</c>) from another
/// state of the CPU state machine (GSH0121) (<see cref="KernelBarrierValidator"/>).
/// </summary>
/// <remarks>
/// [WHY] Whether the goto and the label share a state is decided on the flattened body: lowering detaches some labels
/// (the labels of a statement that holds a split point, the first labels of a reconvergence region) and starts states
/// at others. The check lowers the method as the generator does (<see cref="MethodEmissionPlan"/>) and reports the list
/// that the lowering made (<see cref="LoweredStateMachine.GotosToChainedLabelsAcrossStates"/>), so the two never
/// disagree. It lowers only a method that has a split point, a goto and chained labels, where the managed state machine
/// is emitted: on a project that builds the CPU paths, or a device function of one that exports its device code (its
/// consumers on the CPU paths run that state machine; a CUDA kernel runs as written).
/// The generator reports the same diagnostics from its own lowering (<see cref="HeldRefLocalSite"/>), so a build without
/// the analyzers, or one whose analyzer failed to lower the method, still stops; the compiler shows a diagnostic that
/// both report at one place once.
/// </remarks>
internal static class ChainedLabelJumpChecks
{
    /// <summary>
    /// Reports GSH0121 for every goto that the lowering of the method leaves as a jump into another state.
    /// </summary>
    internal static void Validate(KernelBarrierScan scan)
    {
        if (!scan.HasSplitPoint || scan.Gotos.Count == 0 ||
            !scan.Labels
                .AsStackEnumerable()
                .Any(static label => label.Parent is LabeledStatementSyntax) ||
            !(BarrierUsageAnalyzer.BuildsCpuPaths(scan.Context.Options) ||
              (!scan.IsKernel && ExportsDeviceCode(scan.Context.Options))))
        {
            return;
        }

        LoweredStateMachine? lowered;
        try
        {
            lowered = MethodEmissionPlan.Create(scan.Method, scan.SemanticModel, isKernel: scan.IsKernel).Lowered;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // [WHY] Not swallowed: the generator lowers the same method. When its lowering succeeds it reports GSH0121
            // itself; when it fails it reports GSH0048, or states a method too deep to lower (GSH0102) as #error in the
            // output. Letting the exception escape would fail the whole analyzer (AD0001) and lose the diagnostics of
            // every other method.
            return;
        }

        if (lowered is null)
        {
            return;
        }

        foreach (var (location, jump, crossesBarrier) in FindReported(scan.Method, scan.SemanticModel, lowered))
        {
            // A goto across a barrier is GSH0040's, which CrossBarrierChecks reports
            if (crossesBarrier)
            {
                continue;
            }

            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.GotoToChainedLabelAcrossStates,
                location,
                jump.Label,
                jump.OuterLabel);
        }
    }

    /// <summary>
    /// The gotos of <paramref name="lowered"/> that cannot be lowered
    /// (<see cref="LoweredStateMachine.GotosToChainedLabelsAcrossStates"/>), each with its place in the user's source and
    /// whether it crosses a barrier or a call that suspends: such a goto is GSH0040's
    /// (<see cref="CrossBarrierChecks.IsCrossBarrierGoto"/>), every other one GSH0121's.
    /// </summary>
    /// <remarks>
    /// The one definition for the analyzer and the generator, so both report the same diagnostics; the generator
    /// reports GSH0040 too for the gotos across a barrier, as the analyzer does, since a build without the analyzers
    /// would otherwise pass with the managed body left out. The goto of the
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
            if (location.SourceTree is { } tree &&
                tree.GetRoot().FindNode(location.SourceSpan).FirstAncestorOrSelf<GotoStatementSyntax>() is { } sourceGoto)
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

    // Exported unless the project turns it off, as the generator reads it: the managed state machines of the device
    // code are then emitted for the dialects too
    private static bool ExportsDeviceCode(AnalyzerOptions options) =>
        !(options.AnalyzerConfigOptionsProvider.GlobalOptions
              .TryGetValue("build_property.GSharpExportDeviceCode", out var export) &&
          string.Equals(export, "false", StringComparison.OrdinalIgnoreCase));
}
