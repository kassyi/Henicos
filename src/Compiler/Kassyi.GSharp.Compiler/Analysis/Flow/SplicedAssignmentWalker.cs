using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The walk of <see cref="SplicedFlowBuilder"/> over assignments: the value first, then the target, then the
/// assignment recorded.
/// </summary>
internal static class SplicedAssignmentWalker
{
    // <target> = <value>: the value first; a local target is left to the recorders
    public static void WalkSimpleAssignment(SplicedFlowContext context, ISimpleAssignmentOperation assignment,
        ControlFlowGraph graph)
    {
        foreach (var recorder in context.Recorders)
        {
            recorder.BeforeAssignmentValue(assignment, context.Current.Index);
        }

        SplicedOperationWalker.Walk(context, assignment.Value, graph);
        if (assignment.Target is not ILocalReferenceOperation)
        {
            SplicedOperationWalker.Walk(context, assignment.Target, graph);
        }

        if (assignment.Target is IFlowCaptureReferenceOperation captureRef &&
            assignment.Syntax.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
            context.TryGetCapturedProperty(graph, captureRef.Id, out var capturedProperty) &&
            capturedProperty.Property is { RefKind: RefKind.None, SetMethod: { } setter } &&
            SplicedCallWalker.GetMemberSplit(context.Model.Compilation, setter, capturedProperty.Arguments,
                assignment, graph, capturedProperty.Syntax) is { } setterSplit)
        {
            context.Record((recorder, node) => recorder.OnSplit(setterSplit, node), context.Current.Index);
        }

        if (assignment.Syntax is VariableDeclaratorSyntax
            {
                Parent.Parent: FixedStatementSyntax, Initializer.Value: { } initializer
            } &&
            PinnableReferenceLookup.Find(initializer, context.Model) is { } pinnable)
        {
            SplicedCallWalker.RecordMethodSplitIfSuspending(context,
                pinnable.GetConstructedReducedFrom() ?? pinnable, assignment, graph);
        }

        RecordAssignment(context, assignment, graph);
    }

    // (a, span[i]) = <value>: the value, then the elements that are not plain locals
    public static void WalkDeconstruction(SplicedFlowContext context, IDeconstructionAssignmentOperation deconstruction,
        ControlFlowGraph graph)
    {
        SplicedOperationWalker.Walk(context, deconstruction.Value, graph);
        WalkDeconstructionTarget(context, deconstruction.Target, graph);
        RecordAssignment(context, deconstruction, graph);
    }

    // span[i] = <value>, *p = <value>, x += <value>: the value first, then the target
    public static void WalkCompoundAssignment(SplicedFlowContext context, IAssignmentOperation assignment,
        ControlFlowGraph graph)
    {
        SplicedOperationWalker.Walk(context, assignment.Value, graph);
        SplicedOperationWalker.Walk(context, assignment.Target, graph);
        if (assignment is ICompoundAssignmentOperation compound)
        {
            SplicedCallWalker.RecordMethodSplitIfSuspending(context, compound.InConversion.MethodSymbol, compound,
                graph);
            SplicedCallWalker.RecordMethodSplitIfSuspending(context, compound.OperatorMethod, compound, graph);
            SplicedCallWalker.RecordMethodSplitIfSuspending(context, compound.OutConversion.MethodSymbol, compound,
                graph);
        }

        RecordAssignment(context, assignment, graph);
    }

    private static void WalkDeconstructionTarget(SplicedFlowContext context, IOperation target, ControlFlowGraph graph)
    {
        switch (target)
        {
            case ITupleOperation tuple:
                foreach (var element in tuple.Elements)
                {
                    WalkDeconstructionTarget(context, element, graph);
                }

                return;
            case ILocalReferenceOperation { Local.IsRef: false }:
                return;
            default:
                SplicedOperationWalker.Walk(context, target, graph);
                return;
        }
    }

    private static void RecordAssignment(SplicedFlowContext context, IAssignmentOperation assignment,
        ControlFlowGraph graph)
    {
        foreach (var recorder in context.Recorders)
        {
            recorder.OnAssignment(assignment, graph, context.Current.Index);
        }
    }
}
