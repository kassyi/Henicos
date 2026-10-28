using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

// What must not cross a barrier: recursion into it (GSH0045), jumps (GSH0040) and locals (GSH0039, GSH0041, GSH0072)
internal readonly ref partial struct KernelBarrierValidator
{
    /// <summary>Validates that local functions containing barriers are not recursive (GSH0045).</summary>
    private void ValidateRecursiveLocalFunctions()
    {
        foreach (var localFunc in _localFunctionsWithBarrier)
        {
            var localFuncSymbol = semanticModel.GetDeclaredSymbol<IMethodSymbol>(localFunc);
            if (localFuncSymbol == null)
            {
                continue;
            }

            var isRecursive = localFunc.DescendantNodes()
                .AsStackEnumerable()
                .Any(
                    (Model: semanticModel, Target: localFuncSymbol),
                    static (innerNode, s) =>
                        innerNode is InvocationExpressionSyntax innerInv &&
                        s.Model.GetSymbol(innerInv).IsEqualTo(s.Target)
                );

            if (isRecursive)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.RecursiveBarrierProhibited,
                    localFunc.Identifier.GetLocation(),
                    localFunc.Identifier.ValueText);
            }
        }
    }

    /// <summary>Validates that goto statements do not jump across barriers (GSH0040).</summary>
    private void ValidateCrossBarrierGotos()
    {
        if (_gotos.Count == 0 || _labels.Count == 0)
        {
            return;
        }

        var firstBarrierStart = _barriers[0].SpanStart;
        var lastBarrierEnd = _barriers[_barriers.Count - 1].Span.End;

        foreach (var gotoStmt in _gotos)
        {
            if (gotoStmt.Expression is not IdentifierNameSyntax gotoId)
            {
                continue;
            }

            var labelName = gotoId.Identifier.ValueText;

            var targetLabel = _labels
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

            var firstBarrier = FindFirstBarrierAfter(start);
            if (firstBarrier != null && firstBarrier.Span.End < end)
            {
                context.ReportDiagnostic(
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
    private void ValidateCrossBarrierLocals()
    {
        if (_trackedLocals.Count == 0 || !_hasSplitPoint.Value || !TryCreateControlFlowGraph(out var graph))
        {
            return;
        }

        var locals = new ILocalSymbol[_trackedLocals.Count];
        for (var i = 0; i < locals.Length; i++)
        {
            locals[i] = _trackedLocals[i].Symbol;
        }

        var liveAcrossSplit = CrossBarrierLiveness.FindLiveAcrossSplit(graph, locals, semanticModel);
        for (var i = 0; i < locals.Length; i++)
        {
            if (!liveAcrossSplit[i])
            {
                continue;
            }

            var tracked = _trackedLocals[i];
            if (tracked.IsRefLikeOrRef)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.CrossBarrierRefStructProhibited,
                    tracked.Declarator.Identifier.GetLocation(),
                    tracked.IdentifierText);
            }
            else if (tracked.IsLocalPointer)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.CrossBarrierLocalPointerProhibited,
                    tracked.Declarator.Identifier.GetLocation());
            }
            else if (tracked.IsIteratorLocal)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.IteratorBodyConstructNotSupported,
                    tracked.Declarator.Identifier.GetLocation(),
                    tracked.IdentifierText);
            }
        }
    }

    // The control flow graph of the body; none while the body does not bind (being edited), so nothing is reported then
    private bool TryCreateControlFlowGraph([NotNullWhen(true)] out ControlFlowGraph? graph)
    {
        graph = null;
        if (semanticModel.GetOperation(methodDecl) is not IMethodBodyOperation body)
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

    private InvocationExpressionSyntax? FindFirstBarrierAfter(int position)
    {
        var low = 0;
        var high = _barriers.Count - 1;
        var resultIndex = -1;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (_barriers[mid].SpanStart > position)
            {
                resultIndex = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return resultIndex >= 0 ? _barriers[resultIndex] : null;
    }
}
