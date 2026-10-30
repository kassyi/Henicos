using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// What an analysis records while <see cref="SplicedFlowBuilder"/> walks the operations of a body in the order C#
/// evaluates them (a strategy of the builder). Each method receives the node that the operation is in; the recorder keeps
/// what it needs under that index. The builder decides where bodies and split points go, so every recorder sees the
/// same graph.
/// </summary>
internal interface IFlowRecorder
{
    /// <summary>A node was added; node 0 is the start, where the parameters of the method get their values.</summary>
    void OnNode(int node);

    /// <summary>A local is read (a reference that is not the target of an assignment).</summary>
    void OnLocalReference(ILocalReferenceOperation reference, int node);

    /// <summary>An assignment is about to evaluate its value.</summary>
    void BeforeAssignmentValue(IAssignmentOperation assignment, int node);

    /// <summary>
    /// A simple, compound or deconstruction assignment has evaluated its value and its target; the local references of a
    /// simple assignment target, and of the elements of a deconstruction target that are not ref locals, are left to this
    /// method.
    /// </summary>
    void OnAssignment(IAssignmentOperation assignment, ControlFlowGraph graph, int node);

    /// <summary>An increment or decrement has read its target.</summary>
    void OnIncrement(IIncrementOrDecrementOperation increment, ControlFlowGraph graph, int node);

    /// <summary>A flow capture has evaluated its value.</summary>
    void OnFlowCapture(IFlowCaptureOperation capture, ControlFlowGraph graph, int node);

    /// <summary>An object creation is about to evaluate its arguments.</summary>
    void OnObjectCreation(IObjectCreationOperation creation, int node);

    /// <summary>
    /// A call has evaluated its receiver and arguments (and run the local function it calls); a <c>Gpu</c> macro its
    /// arguments other than its body.
    /// </summary>
    void OnCall(IInvocationOperation invocation, int node);

    /// <summary>The state machine splits here.</summary>
    void OnSplit(SplitPoint split, int node);

    /// <summary>A call has returned: it writes its <c>ref</c> and <c>out</c> arguments now.</summary>
    void OnCallReturned(IInvocationOperation invocation, ControlFlowGraph graph, int node);

    /// <summary>A body is about to be spliced in after node.</summary>
    void OnBodyEntered(SplicedBody body, int node);

    /// <summary>A body was spliced in; node follows it.</summary>
    void OnBodyExited(SplicedBody body, int node);

    /// <summary>An operation of a basic block (or its branch value) was walked.</summary>
    void OnTopOperation(IOperation operation, ControlFlowGraph graph, int node);
}
