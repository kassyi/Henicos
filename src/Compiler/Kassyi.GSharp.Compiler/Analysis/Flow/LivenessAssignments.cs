using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// What an assignment writes for <see cref="LivenessRecorder"/>: a local, a local or tracked parameter reached through
/// a flow capture, or the elements of a deconstruction; and which references are the whole target of one.
/// </summary>
internal static class LivenessAssignments
{
    /// <summary>The writes of <paramref name="assignment"/>, at <paramref name="node"/>.</summary>
    public static void Record(LivenessEventLog log, FlowCaptureTable flowCaptures, IAssignmentOperation assignment,
        int node)
    {
        switch (assignment)
        {
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } simple:
                log.AddAssigned(target.Local, target.Syntax, node,
                    replaces: target.IsDeclaration || simple.IsRef || !target.Local.IsRef);
                break;
            case ISimpleAssignmentOperation { Target: var target } simple
                when flowCaptures.TryGetWholeLocal(target) is { } local:
                log.AddAssigned(local, target.Syntax, node, replaces: simple.IsRef || !local.IsRef);
                break;
            case ISimpleAssignmentOperation { Target: var target }
                when flowCaptures.IndexOfWholeParameter(target, log.Index) is var index and >= 0:
                log.AddParameterAssigned(index, target.Syntax, node);
                break;
            case IDeconstructionAssignmentOperation deconstruction:
                RecordDeconstructed(log, flowCaptures, deconstruction.Target, node);
                break;
        }
    }

    /// <summary>Whether <paramref name="reference"/> is the whole target of an assignment or deconstruction.</summary>
    public static bool IsWrittenWhole(IParameterReferenceOperation reference) => reference.Parent switch
    {
        ISimpleAssignmentOperation assignment => assignment.Target == reference,
        ITupleOperation tuple => ParameterChanges.IsDeconstructionTarget(tuple),
        _ => IsCapturedSimpleAssignmentLeft(reference)
    };

    /// <summary>
    /// Whether <paramref name="reference"/> is a whole variable captured on the left of a simple assignment.
    /// </summary>
    public static bool IsCapturedSimpleAssignmentLeft(IOperation reference) =>
        reference.Parent is IFlowCaptureOperation &&
        reference.Syntax.Parent is AssignmentExpressionSyntax
        {
            RawKind: (int)SyntaxKind.SimpleAssignmentExpression
        } assignment &&
        assignment.Left == reference.Syntax;

    // The elements of a deconstruction target, nested tuples included
    private static void RecordDeconstructed(LivenessEventLog log, FlowCaptureTable flowCaptures, IOperation target,
        int node)
    {
        switch (target)
        {
            case ITupleOperation tuple:
                foreach (var element in tuple.Elements)
                {
                    RecordDeconstructed(log, flowCaptures, element, node);
                }

                break;
            case ILocalReferenceOperation { Local: { IsRef: false } local }:
                log.AddAssigned(local, target.Syntax, node, replaces: true);
                break;
            case var _ when flowCaptures.TryGetWholeLocal(target) is { IsRef: false } capturedLocal:
                log.AddAssigned(capturedLocal, target.Syntax, node, replaces: true);
                break;
            case var _ when flowCaptures.IndexOfWholeParameter(target, log.Index) is var index and >= 0:
                log.AddParameterAssigned(index, target.Syntax, node);
                break;
        }
    }
}
