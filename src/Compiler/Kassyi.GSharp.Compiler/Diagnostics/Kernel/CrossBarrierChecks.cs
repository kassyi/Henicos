using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>What must not cross a barrier: recursion into it (GSH0045), jumps (GSH0040) and locals (GSH0039, GSH0041, GSH0072) (<see cref="KernelBarrierValidator"/>).</summary>
internal static class CrossBarrierChecks
{
    /// <summary>Validates that local functions containing barriers are not recursive (GSH0045).</summary>
    internal static void ValidateRecursiveLocalFunctions(KernelBarrierScan scan)
    {
        foreach (var localFunc in scan.LocalFunctionsWithBarrier)
        {
            var localFuncSymbol = scan.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(localFunc);
            if (localFuncSymbol == null)
            {
                continue;
            }

            var isRecursive = localFunc.DescendantNodes()
                .AsStackEnumerable()
                .Any(
                    (Model: scan.SemanticModel, Target: localFuncSymbol),
                    static (innerNode, s) =>
                        innerNode is InvocationExpressionSyntax innerInv &&
                        s.Model.GetSymbol(innerInv).IsEqualTo(s.Target)
                );

            if (isRecursive)
            {
                scan.Context.ReportDiagnostic(
                    GSharpDiagnostics.RecursiveBarrierProhibited,
                    localFunc.Identifier.GetLocation(),
                    localFunc.Identifier.ValueText);
            }
        }
    }

    /// <summary>Validates that goto statements do not jump across barriers (GSH0040).</summary>
    internal static void ValidateCrossBarrierGotos(KernelBarrierScan scan)
    {
        if (scan.Gotos.Count == 0 || scan.Labels.Count == 0)
        {
            return;
        }

        foreach (var gotoStmt in scan.Gotos)
        {
            if (FindTargetLabel(scan.Labels, gotoStmt, scan.SemanticModel) is { } targetLabel &&
                IsCrossBarrierGoto(scan.Barriers, gotoStmt, targetLabel))
            {
                scan.Context.ReportDiagnostic(
                    GSharpDiagnostics.CrossBarrierGotoProhibited,
                    gotoStmt.GetLocation());
            }
        }
    }

    /// <summary>
    /// The label among <paramref name="labels"/> that <paramref name="gotoStmt"/> jumps to: the one C# binds the goto to,
    /// since sibling blocks may declare the same label name; <see langword="null"/> for <c>goto case</c> and
    /// <c>goto default</c>.
    /// </summary>
    /// <remarks>
    /// The one lookup of GSH0040 (the analyzer) and of GSH0121 (<see cref="ChainedLabelJumpChecks.FindReported"/>).
    /// </remarks>
    internal static LabeledStatementSyntax? FindTargetLabel(IReadOnlyList<LabeledStatementSyntax> labels,
        GotoStatementSyntax gotoStmt, SemanticModel semanticModel) =>
        gotoStmt.IsKind(SyntaxKind.GotoStatement) &&
        semanticModel.GetSymbol(gotoStmt.Expression) is ILabelSymbol target
            ? labels
                .AsStackEnumerable()
                .FirstOrDefault((target, semanticModel), static (label, state) =>
                    SymbolEqualityComparer.Default.Equals(
                        state.semanticModel.GetDeclaredSymbol<ILabelSymbol>(label), state.target)
                )
            : null;

    /// <summary>
    /// Whether a goto to <paramref name="targetLabel"/> crosses one of <paramref name="barriers"/> (GSH0040): one that
    /// starts inside the range from the earlier of the two to the later one and ends before the range does.
    /// </summary>
    /// <param name="barriers">
    /// The block barriers and the calls that suspend of the method, in text order
    /// (<see cref="KernelConstructChecks.IsBarrierCall"/>).
    /// </param>
    /// <remarks>
    /// The one test of GSH0040 and of GSH0121 (<see cref="ChainedLabelJumpChecks.FindReported"/>): a goto that GSH0040
    /// reports, from the analyzer alone, is left out of GSH0121 (the generator's), so each goto gets one of them.
    /// </remarks>
    internal static bool IsCrossBarrierGoto(IReadOnlyList<InvocationExpressionSyntax> barriers,
        GotoStatementSyntax gotoStmt, LabeledStatementSyntax targetLabel)
    {
        var start = Math.Min(gotoStmt.SpanStart, targetLabel.SpanStart);
        var end = Math.Max(gotoStmt.Span.End, targetLabel.Span.End);

        if (barriers.Count == 0 || end <= barriers[0].SpanStart || start >= barriers[barriers.Count - 1].Span.End)
        {
            return false;
        }

        var firstBarrier = FindFirstBarrierAfter(barriers, start);
        return firstBarrier != null && firstBarrier.Span.End < end;
    }

    /// <summary>
    /// Validates that ref structs, local pointers, and iterator locals are not live across a point where the state machine
    /// splits (GSH0039, GSH0041, GSH0072): some path from the split uses them before assigning them again
    /// (<see cref="CrossBarrierLiveness"/>). The state machine cannot keep such a local in the thread's frame.
    /// A by-value ref struct parameter cannot be kept there either; it is passed again on resume, so it is reported
    /// only when it is changed before a split and used after it (GSH0039, #965).
    /// </summary>
    internal static void ValidateCrossBarrierLocals(KernelBarrierScan scan)
    {
        var locals = CrossBarrierLocals.Find(scan);
        if (locals.IsEmpty)
        {
            return;
        }

        var hasLiftedCandidate = false;
        foreach (var local in locals)
        {
            if (local.Descriptor.Id == GSharpDiagnostics.IteratorBodyConstructNotSupported.Id)
            {
                scan.Context.ReportDiagnostic(local.Descriptor, local.Location, local.Name);
            }
            else
            {
                hasLiftedCandidate = true;
            }
        }

        if (!hasLiftedCandidate || scan.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(scan.Method) is not { } method)
        {
            return;
        }

        var models = CpuBarrierModels.Read(scan.Context.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
        if (StackOnlyExecution.FindStateMachineReason(method, scan.Method, scan.SemanticModel, models) is not { } reason)
        {
            return;
        }

        foreach (var local in locals)
        {
            if (local.Descriptor.Id == GSharpDiagnostics.CrossBarrierRefStructProhibited.Id)
            {
                scan.Context.ReportDiagnostic(
                    local.Descriptor,
                    local.Location,
                    local.Name,
                    StackOnlyExecution.FormatRefStructReason(method, reason, models));
            }
            else if (local.Descriptor.Id == GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id)
            {
                scan.Context.ReportDiagnostic(
                    local.Descriptor,
                    local.Location,
                    StackOnlyExecution.FormatLocalPointerReason(method, reason, models));
            }
        }
    }

    private static InvocationExpressionSyntax? FindFirstBarrierAfter(IReadOnlyList<InvocationExpressionSyntax> barriers,
        int position)
    {
        var low = 0;
        var high = barriers.Count - 1;
        var resultIndex = -1;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (barriers[mid].SpanStart > position)
            {
                resultIndex = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return resultIndex >= 0 ? barriers[resultIndex] : null;
    }
}
