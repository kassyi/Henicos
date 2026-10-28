using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

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

        var firstBarrierStart = scan.Barriers[0].SpanStart;
        var lastBarrierEnd = scan.Barriers[scan.Barriers.Count - 1].Span.End;

        foreach (var gotoStmt in scan.Gotos)
        {
            if (gotoStmt.Expression is not IdentifierNameSyntax gotoId)
            {
                continue;
            }

            var labelName = gotoId.Identifier.ValueText;

            var targetLabel = scan.Labels
                .AsStackEnumerable()
                .FirstOrDefault(labelName, static (l, name) => l.Identifier.ValueText == name);

            if (targetLabel == null)
            {
                continue;
            }

            var start = Math.Min(gotoStmt.SpanStart, targetLabel.SpanStart);
            var end = Math.Max(gotoStmt.Span.End, targetLabel.Span.End);

            if (end <= firstBarrierStart || start >= lastBarrierEnd)
            {
                continue;
            }

            var firstBarrier = FindFirstBarrierAfter(scan, start);
            if (firstBarrier != null && firstBarrier.Span.End < end)
            {
                scan.Context.ReportDiagnostic(
                    GSharpDiagnostics.CrossBarrierGotoProhibited,
                    gotoStmt.GetLocation());
            }
        }
    }

    /// <summary>
    /// Validates that ref structs, local pointers, and iterator locals are not live across a point where the state machine
    /// splits (GSH0039, GSH0041, GSH0072): some path from the split uses them before assigning them again
    /// (<see cref="CrossBarrierLiveness"/>). The state machine cannot keep such a local in the thread's frame.
    /// </summary>
    internal static void ValidateCrossBarrierLocals(KernelBarrierScan scan)
    {
        if (scan.TrackedLocals.Count == 0 || !scan.HasSplitPoint || !TryCreateControlFlowGraph(scan, out var graph))
        {
            return;
        }

        var locals = new ILocalSymbol[scan.TrackedLocals.Count];
        for (var i = 0; i < locals.Length; i++)
        {
            locals[i] = scan.TrackedLocals[i].Symbol;
        }

        var liveAcrossSplit = CrossBarrierLiveness.FindLiveAcrossSplit(graph, locals, scan.SemanticModel);
        for (var i = 0; i < locals.Length; i++)
        {
            if (!liveAcrossSplit[i])
            {
                continue;
            }

            var tracked = scan.TrackedLocals[i];
            if (tracked.IsRefLikeOrRef)
            {
                scan.Context.ReportDiagnostic(
                    GSharpDiagnostics.CrossBarrierRefStructProhibited,
                    tracked.Declarator.Identifier.GetLocation(),
                    tracked.IdentifierText);
            }
            else if (tracked.IsLocalPointer)
            {
                scan.Context.ReportDiagnostic(
                    GSharpDiagnostics.CrossBarrierLocalPointerProhibited,
                    tracked.Declarator.Identifier.GetLocation());
            }
            else if (tracked.IsIteratorLocal)
            {
                scan.Context.ReportDiagnostic(
                    GSharpDiagnostics.IteratorBodyConstructNotSupported,
                    tracked.Declarator.Identifier.GetLocation(),
                    tracked.IdentifierText);
            }
        }
    }

    // The control flow graph of the body; none while the body does not bind (being edited), so nothing is reported then
    private static bool TryCreateControlFlowGraph(KernelBarrierScan scan, [NotNullWhen(true)] out ControlFlowGraph? graph)
    {
        graph = null;
        if (scan.SemanticModel.GetOperation(scan.Method) is not IMethodBodyOperation body)
        {
            return false;
        }

        foreach (var operation in body.Descendants())
        {
            if (operation is IInvalidOperation)
            {
                return false;
            }
        }

        graph = ControlFlowGraph.Create(body);
        return true;
    }

    private static InvocationExpressionSyntax? FindFirstBarrierAfter(KernelBarrierScan scan, int position)
    {
        var low = 0;
        var high = scan.Barriers.Count - 1;
        var resultIndex = -1;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (scan.Barriers[mid].SpanStart > position)
            {
                resultIndex = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return resultIndex >= 0 ? scan.Barriers[resultIndex] : null;
    }
}
