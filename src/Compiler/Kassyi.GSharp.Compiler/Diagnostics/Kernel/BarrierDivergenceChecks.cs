namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>Where a barrier stands: divergence (GSH0037), <c>fixed</c> (GSH0043), <c>using</c> scopes (GSH0003) and local functions (GSH0045) (<see cref="KernelBarrierValidator"/>).</summary>
internal static class BarrierDivergenceChecks
{
    /// <summary>
    /// Validates the syntactic ancestors of a barrier invocation (GSH0043, GSH0045, and GSH0003 for a barrier in the
    /// scope of a <c>using</c>): rules about where the barrier is written, not about the control flow.
    /// </summary>
    /// <param name="scan">The state of the run.</param>
    /// <param name="barrier">A block barrier, or a call to a device function that reaches a barrier.</param>
    internal static void InspectBarrierAncestors(KernelBarrierScan scan, InvocationExpressionSyntax barrier)
    {
        for (var ancestor = barrier.Parent; ancestor != null && ancestor != scan.Method; ancestor = ancestor.Parent)
        {
            switch (ancestor)
            {
                // GSH0043: fixed block barrier prohibited
                case FixedStatementSyntax:
                    scan.Context.ReportDiagnostic(
                        GSharpDiagnostics.FixedBlockBarrierProhibited,
                        barrier.GetLocation());
                    break;
                // GSH0003: the state machine would leave the scope of the resource at the barrier and dispose it
                case UsingStatementSyntax:
                case BlockSyntax block when IsInUsingDeclarationScope(block, barrier):
                    scan.Context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedSyntax,
                        barrier.GetLocation(),
                        GpuCodeConstraints.BarrierInUsingScope);
                    break;
                // GSH0045: Queue local function containing barrier for recursion check
                case LocalFunctionStatementSyntax localFunc:
                    {
                        if (!scan.LocalFunctionsWithBarrier.Contains(localFunc))
                        {
                            scan.LocalFunctionsWithBarrier.Add(localFunc);
                        }

                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Reports a barrier, or a call that reaches one, that a thread-variant branch lets only some threads reach while
    /// the others reach another split point (<see cref="BarrierDivergence.Condition"/>): the branch may be in this
    /// method, or in the callee, depending on the arguments or leaving it early (§3.2.4). It is GSH0037 (error) when
    /// the branch certainly diverges (<see cref="BarrierReport.Certain"/>), GSH0083 (warning) otherwise. A barrier that
    /// the others skip only on their way out of the method (<see cref="BarrierDivergence.ExitOnly"/>) is not reported
    /// here.
    /// A barrier written in a body that the CPU dialects do not split (a lambda other than the bodies of
    /// <c>Gpu.ForUnroll</c> and <c>Gpu.SyncGrid(body)</c>, or a local function) is always GSH0037.
    /// </summary>
    internal static void ValidateDivergentBarriers(KernelBarrierScan scan)
    {
        if (scan.SyncPoints.Count == 0)
        {
            return;
        }

        UniformityAnalysis? uniformity = null;

        // GSH0084 in a kernel: each condition that sends threads out before a barrier, with the first barrier it skips
        var exits = new Dictionary<SyntaxNode, (InvocationExpressionSyntax Barrier, SyncScope Scope)>();
        foreach (var (barrier, scope, isCall) in scan.SyncPoints)
        {
            if (!isCall && IsInUnsplitBody(scan, barrier))
            {
                ReportUnsplitBarrier(scan, barrier);
                continue;
            }

            if (uniformity == null && !scan.Flow.TryGetUniformity(out uniformity))
            {
                return;
            }

            var report = GetReport(uniformity, barrier, scope, isCall);
            if (report.Divergence == BarrierDivergence.Condition)
            {
                ReportDivergentBarrier(scan, barrier, scope, report.Certain);
            }
            else if (scan.IsKernel)
            {
                CollectExits(scan, uniformity, barrier, scope, isCall, exits);
            }
        }

        ReportExits(scan, exits);
    }

    // The divergence of a barrier in this method; for a call, the call site's when it diverges there, or when the
    // method itself says nothing
    private static BarrierReport GetReport(UniformityAnalysis uniformity, InvocationExpressionSyntax barrier,
        SyncScope scope, bool isCall)
    {
        var report = uniformity.GetBarrierReport(barrier, scope);
        if (!isCall || report.Divergence == BarrierDivergence.Condition)
        {
            return report;
        }

        var callSite = uniformity.GetCallSiteReport(barrier);
        return callSite.Divergence == BarrierDivergence.Condition || report.Divergence == BarrierDivergence.None
            ? callSite
            : report;
    }

    // GSH0037 when the branch certainly diverges, GSH0083 when it depends on the launch or on run-time values
    private static void ReportDivergentBarrier(KernelBarrierScan scan, InvocationExpressionSyntax barrier,
        SyncScope scope, bool certain)
    {
        if (certain)
        {
            scan.Context.ReportDiagnostic(GSharpDiagnostics.DivergentBarrierProhibited, barrier.GetLocation(),
                DivergentBarrierMessage);
            return;
        }

        scan.Context.ReportDiagnostic(GSharpDiagnostics.LaunchDependentDivergentBarrier, barrier.GetLocation(),
            scan.SemanticModel.GetSymbol(barrier)?.Name ?? "", scope == SyncScope.Grid ? "grid" : "block");
    }

    // GSH0037 for a barrier in a lambda or local function that the state machine does not split
    private static void ReportUnsplitBarrier(KernelBarrierScan scan, InvocationExpressionSyntax barrier) =>
        scan.Context.ReportDiagnostic(GSharpDiagnostics.DivergentBarrierProhibited, barrier.GetLocation(),
            $"'{scan.SemanticModel.GetSymbol(barrier)?.Name}' cannot be called in a lambda or local function other " +
            "than the body of Gpu.ForUnroll or Gpu.SyncGrid(body): the CPU dialects resume a kernel only in " +
            "the statements of its body and of those macro bodies, so the barrier would not wait for the " +
            "other threads there.");

    // An exit-only barrier of a kernel: its exit conditions, or the call whose callee the threads leave early
    private static void CollectExits(KernelBarrierScan scan, UniformityAnalysis uniformity,
        InvocationExpressionSyntax barrier, SyncScope scope, bool isCall,
        Dictionary<SyntaxNode, (InvocationExpressionSyntax Barrier, SyncScope Scope)> exits)
    {
        foreach (var branch in uniformity.GetExitBranches(barrier, scope))
        {
            // A case of a switch statement compares with the case value; the switch expression is what diverges
            var condition = branch.FirstAncestorOrSelf<SwitchLabelSyntax>() is
                { Parent: SwitchSectionSyntax { Parent: SwitchStatementSyntax switchStatement } }
                ? switchStatement.Expression
                : branch;
            if (!exits.TryGetValue(condition, out var first) || barrier.SpanStart < first.Barrier.SpanStart)
            {
                exits[condition] = (barrier, scope);
            }
        }

        if (isCall && uniformity.IsExitOnlyCall(barrier))
        {
            var callee = scan.SemanticModel.GetSymbol(barrier)?.Name ?? "";
            scan.Context.ReportDiagnostic(GSharpDiagnostics.BarrierAfterDivergentExit, barrier.GetLocation(),
                $"Some threads leave '{callee}' early", $"a barrier inside '{callee}'", "block");
        }
    }

    // GSH0084 at each exit condition, with the first barrier it skips as the additional location
    private static void ReportExits(KernelBarrierScan scan,
        Dictionary<SyntaxNode, (InvocationExpressionSyntax Barrier, SyncScope Scope)> exits)
    {
        foreach (var pair in exits)
        {
            var (barrier, scope) = pair.Value;
            scan.Context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.BarrierAfterDivergentExit,
                pair.Key.GetLocation(),
                [barrier.GetLocation()],
                "Where this condition holds, a thread leaves the kernel",
                $"'{scan.SemanticModel.GetSymbol(barrier)?.Name}'",
                scope == SyncScope.Grid ? "grid" : "block"));
        }
    }

    private const string DivergentBarrierMessage =
        "Gpu.SyncThreads(), or a call to a device function that reaches it, cannot be placed inside a conditional " +
        "branch or an operand that only some threads evaluate ('&&', '||', '??', '?:'). This causes undefined " +
        "behavior on the GPU and silently reorders shared memory accesses on the CPU emulator.";

    // [WHY] The state machine of the CPU dialects splits only the statements of the method and the inlined macro
    // bodies; a barrier in any other lambda or in a local function cannot be resumed (decision 1.5)
    private static bool IsInUnsplitBody(KernelBarrierScan scan, InvocationExpressionSyntax barrier)
    {
        for (var ancestor = barrier.Parent; ancestor != null && ancestor != scan.Method; ancestor = ancestor.Parent)
        {
            if (ancestor is LocalFunctionStatementSyntax ||
                (ancestor is AnonymousFunctionExpressionSyntax &&
                 !BarrierUsageAnalyzer.IsInlinedMacroBody(ancestor, scan.SemanticModel)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="barrier"/> in <paramref name="block"/> is in the scope of a <c>using</c> declaration of the
    /// block: one declared before it, or any one when the block is a nested block, whose statements the lowering moves
    /// into the enclosing block (BranchRewriter, LoopRewriter) so that its scope would end there.
    /// </summary>
    private static bool IsInUsingDeclarationScope(BlockSyntax block, InvocationExpressionSyntax barrier)
    {
        var isNested = block.Parent is BlockSyntax;
        return block.Statements
            .AsStackEnumerable()
            .Any((barrier.SpanStart, isNested), static (statement, state) =>
                UsingTranspiler.IsUsingDeclaration(statement) &&
                (state.isNested || statement.SpanStart < state.SpanStart));
    }
}
