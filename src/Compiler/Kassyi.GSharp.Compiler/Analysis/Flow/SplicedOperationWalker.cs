using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The walk of <see cref="SplicedFlowBuilder"/> over the operations of a block in the order C# evaluates them: the
/// dispatch by operation kind, and the references, increments, flow captures and object creations. Assignments are
/// <see cref="SplicedAssignmentWalker"/>'s, calls <see cref="SplicedCallWalker"/>'s and bodies
/// <see cref="SplicedBodySplicer"/>'s.
/// </summary>
internal static class SplicedOperationWalker
{
    /// <summary>
    /// An operation of a block: in a <c>Gpu.SyncGrid</c> body, a split comes first when a gap lies between it and the
    /// operation before it in the same block.
    /// </summary>
    public static void WalkTop(SplicedFlowContext context, IOperation operation, ControlFlowGraph graph)
    {
        context.TopRange = context.Gaps?.RangeOf(operation.Syntax);
        if (context.TopRange is { } range && context.Gaps is { } gaps)
        {
            if (context.LastRange is { } last && gaps.HasGap(last.Last, range.First))
            {
                context.Record(
                    static (recorder, node) =>
                        recorder.OnSplit(new SplitPoint(SplitKind.Gap, null, null, null, null), node),
                    context.Current.Index
                );
            }

            context.Current.First ??= range;
        }

        Walk(context, operation, graph);
        foreach (var recorder in context.Recorders)
        {
            recorder.OnTopOperation(operation, graph, context.Current.Index);
        }

        if (context.TopRange is { } walked)
        {
            context.Current.First ??= walked;
            context.Current.Last = walked;
            context.LastRange = walked;
        }
    }

    /// <summary>The operations of <paramref name="operation"/> in the order C# evaluates them.</summary>
    public static void Walk(SplicedFlowContext context, IOperation operation, ControlFlowGraph graph)
    {
        switch (operation)
        {
            case IInvocationOperation invocation:
                SplicedCallWalker.WalkInvocation(context, invocation, graph);
                return;
            case IFlowAnonymousFunctionOperation function:
                SplicedBodySplicer.SpliceLambda(context, function, graph);
                return;
            case IDelegateCreationOperation
            {
                Target: IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction } reference
            }:
                SplicedBodySplicer.SpliceLocalFunction(context, reference.Method.OriginalDefinition, graph,
                    SplicedBodyKind.LocalFunctionDelegate);
                return;
            case ILocalReferenceOperation reference:
                WalkLocalReference(context, reference);
                return;
            case IParameterReferenceOperation reference:
                WalkParameterReference(context, reference);
                return;
            case IFlowCaptureReferenceOperation reference:
                WalkFlowCaptureReference(context, reference);
                return;
            case ISimpleAssignmentOperation assignment:
                SplicedAssignmentWalker.WalkSimpleAssignment(context, assignment, graph);
                return;
            case IDeconstructionAssignmentOperation deconstruction:
                SplicedAssignmentWalker.WalkDeconstruction(context, deconstruction, graph);
                return;
            case IAssignmentOperation assignment:
                SplicedAssignmentWalker.WalkCompoundAssignment(context, assignment, graph);
                return;
            case IIncrementOrDecrementOperation increment:
                WalkIncrement(context, increment, graph);
                return;
            case IFlowCaptureOperation capture:
                WalkFlowCapture(context, capture, graph);
                return;
            case IObjectCreationOperation creation:
                WalkObjectCreation(context, creation, graph);
                return;
        }

        WalkChildren(context, operation, graph);
    }

    private static void WalkChildren(SplicedFlowContext context, IOperation operation, ControlFlowGraph graph)
    {
        foreach (var child in operation.ChildOperations)
        {
            Walk(context, child, graph);
        }
    }

    private static void WalkLocalReference(SplicedFlowContext context, ILocalReferenceOperation reference)
    {
        foreach (var recorder in context.Recorders)
        {
            recorder.OnLocalReference(reference, context.Current.Index);
        }
    }

    private static void WalkParameterReference(SplicedFlowContext context, IParameterReferenceOperation reference)
    {
        foreach (var recorder in context.Recorders)
        {
            recorder.OnParameterReference(reference, context.Current.Index);
        }
    }

    private static void WalkFlowCaptureReference(SplicedFlowContext context, IFlowCaptureReferenceOperation reference)
    {
        foreach (var recorder in context.Recorders)
        {
            recorder.OnFlowCaptureReference(reference, context.Current.Index);
        }
    }

    private static void WalkIncrement(SplicedFlowContext context, IIncrementOrDecrementOperation increment,
        ControlFlowGraph graph)
    {
        Walk(context, increment.Target, graph);
        foreach (var recorder in context.Recorders)
        {
            recorder.OnIncrement(increment, graph, context.Current.Index);
        }
    }

    private static void WalkFlowCapture(SplicedFlowContext context, IFlowCaptureOperation capture,
        ControlFlowGraph graph)
    {
        Walk(context, capture.Value, graph);
        foreach (var recorder in context.Recorders)
        {
            recorder.OnFlowCapture(capture, graph, context.Current.Index);
        }
    }

    private static void WalkObjectCreation(SplicedFlowContext context, IObjectCreationOperation creation,
        ControlFlowGraph graph)
    {
        foreach (var recorder in context.Recorders)
        {
            recorder.OnObjectCreation(creation, context.Current.Index);
        }

        WalkChildren(context, creation, graph);
    }
}
