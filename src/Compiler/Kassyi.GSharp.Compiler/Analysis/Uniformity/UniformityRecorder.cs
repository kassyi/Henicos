using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Records, for <see cref="UniformityAnalysis"/>, what the operations of each node of a <see cref="SplicedFlowGraph"/>
/// write and evaluate, where the parameters get their variance, which calls and object creations each node evaluates,
/// and where the barriers and barrier-reaching calls are.
/// </summary>
/// <remarks>
/// <para>
/// A spliced lambda or local function body is opaque (<see cref="SplicedBody.IsOpaque"/>): its parameters are
/// thread-variant, as its callers are not followed, and its events carry the body, as an assignment in it to a local
/// declared outside it only adds to the variance. A local function body spliced where it is called runs with that call:
/// its parameters get the variance of the arguments, and the value it returns becomes the result of the call. The index
/// of a <c>Gpu.ForUnroll</c> body counts the same in every thread.
/// </para>
/// <para>
/// The value indices are kept in <see cref="UniformityValues"/>, the bodies being built in
/// <see cref="UniformityBodies"/>; the walk over what an operation writes is <see cref="UniformityWrites"/>'s.
/// </para>
/// </remarks>
/// <param name="method">The analyzed method.</param>
/// <param name="masks">Which parameters of <paramref name="method"/> receive a thread-variant argument.</param>
internal sealed class UniformityRecorder(IMethodSymbol method, ArgumentMasks masks) : IFlowRecorder
{
    private readonly UniformityBodies _bodies = new();

    private UniformityValues? _values;

    private MemoryUniformity? _memory;

    public ImmutableArray<UniformityNode>.Builder Nodes { get; } = ImmutableArray.CreateBuilder<UniformityNode>();

    /// <summary>The nodes where a call or object creation is evaluated.</summary>
    public Dictionary<SyntaxNode, ImmutableArray<int>.Builder> NodesOf { get; } = new();

    /// <summary>The nodes where a call that splits the state machine is.</summary>
    public Dictionary<SyntaxNode, ImmutableArray<int>.Builder> SplitsAt { get; } = new();

    /// <summary>The barriers and barrier-reaching calls of the graph.</summary>
    public Dictionary<SyntaxNode, BarrierSite> Sites { get; } = new();

    /// <summary>
    /// The memory that the threads share, whose values follow the locals, parameters and flow captures.
    /// </summary>
    public MemoryUniformity Memory => _memory ??= new MemoryUniformity(this);

    /// <summary>The number of values: locals, parameters, flow captures, local function results and memory.</summary>
    public int ValueCount => Values.Count + Memory.Count;

    private UniformityValues Values => _values ??= new UniformityValues(this);

    /// <summary>The value index of a local or parameter, when the graph writes it.</summary>
    public bool TryGetValue(ISymbol symbol, out int index) => Values.TryGetValue(symbol, out index);

    /// <summary>
    /// The value index of the result of a local function call, when its body is spliced where it is called.
    /// </summary>
    public bool TryGetResult(SyntaxNode call, out int index) => Values.TryGetResult(call, out index);

    /// <summary>The value index of a flow capture, when the graph writes it.</summary>
    public bool TryGetCapture(ControlFlowGraph graph, CaptureId id, out int index) =>
        Values.TryGetCapture(graph, id, out index);

    /// <summary>The value index of a flow capture, given one on first use.</summary>
    public int ValueOf(ControlFlowGraph graph, CaptureId id) => Values.ValueOf(graph, id);

    // Node 0 is the start: the parameters of the method get their variance from the masks
    public void OnNode(int node)
    {
        Nodes.Add(new UniformityNode());
        if (node != 0)
        {
            return;
        }

        Memory.OnStart(method, node);
        foreach (var parameter in method.Parameters)
        {
            var bit = ArgumentMasks.Bit(parameter.Ordinal);
            var variance = (masks.Block & bit) != 0 ? ThreadVariance.Block :
                (masks.Grid & bit) != 0 ? ThreadVariance.Grid : ThreadVariance.Uniform;
            Define(parameter, variance, node);
        }
    }

    public void OnLocalReference(ILocalReferenceOperation reference, int node)
    {
    }

    public void OnParameterReference(IParameterReferenceOperation reference, int node)
    {
    }

    public void OnFlowCaptureReference(IFlowCaptureReferenceOperation reference, int node)
    {
    }

    public void BeforeAssignmentValue(IAssignmentOperation assignment, int node)
    {
    }

    public void OnAssignment(IAssignmentOperation assignment, ControlFlowGraph graph, int node)
    {
        AddWrite(assignment, graph, node);
        Memory.AddAssignment(assignment, node);
    }

    public void OnIncrement(IIncrementOrDecrementOperation increment, ControlFlowGraph graph, int node)
    {
        AddWrite(increment, graph, node);
        Memory.AddWrite(increment.Target, node);
    }

    public void OnFlowCapture(IFlowCaptureOperation capture, ControlFlowGraph graph, int node)
    {
        UniformityWrites.AddWritten(Nodes[node], ValueOf(graph, capture.Id));
        AddWrite(capture, graph, node);
    }

    public void OnObjectCreation(IObjectCreationOperation creation, int node) =>
        RecordNode(NodesOf, creation.Syntax, node);

    public void OnCall(IInvocationOperation invocation, int node)
    {
        RecordNode(NodesOf, invocation.Syntax, node);
        Memory.AddCall(invocation, node);
    }

    // A call that splits the state machine; a barrier, or a call that reaches one, is also a site of GSH0037. The gaps
    // inside a Gpu.SyncGrid(body) are not split points here: the call is (a grid barrier). The memory, though, is
    // synced in each gap, where the expansion places the grid sync, and not where the body starts.
    public void OnSplit(SplitPoint split, int node)
    {
        if (split.Kind == SplitKind.Gap)
        {
            Memory.AddSync(SyncScope.Grid, node);
            return;
        }

        if (split is not { Syntax: { } call, Graph: { } graph })
        {
            return;
        }

        Nodes[node].HasSplit = true;
        Nodes[node].Splits.Add(call);
        RecordNode(SplitsAt, call, node);
        if (split.BarrierScope is not { } scope)
        {
            return;
        }

        Sites[call] = new BarrierSite(scope, split.BarrierCall, graph);
        if (split.BarrierCall is { } invocation)
        {
            Nodes[node].Events.Add(new UniformityEvent(UniformityEventKind.Site, invocation, graph, _bodies.Opaque));
        }
        else if (split.Kind == SplitKind.Call)
        {
            // A barrier; the callee of a barrier-reaching call lowers the memory through its summary (AddCall)
            Memory.AddSync(scope, node);
        }
    }

    // M(ref x), M(out y): written when the call returns
    public void OnCallReturned(IInvocationOperation invocation, ControlFlowGraph graph, int node)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                AddWrite(invocation, graph, node);
                return;
            }
        }
    }

    // The parameters of a lambda or local function are thread-variant, except the index of a ForUnroll body and those
    // of a local function that receive an argument of the call it runs for
    public void OnBodyEntered(SplicedBody body, int node)
    {
        var parameters = body.Parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            var isIndex = body.HasUniformIndex && i == parameters.Length - 1;
            Define(parameters[i], isIndex ? ThreadVariance.Uniform : ThreadVariance.Block, node);
        }

        if (body is { Call: { } call, CallGraph: { } callGraph })
        {
            BindArguments(call, callGraph, parameters, node);
            Values.ResultOf(call.Syntax);
        }

        _bodies.Push(body);
    }

    public void OnBodyExited(SplicedBody body, int node) => _bodies.Pop(body);

    // return <value> of a local function body spliced where it is called: the result of that call
    public void OnBodyReturn(IOperation value, ControlFlowGraph graph, int node)
    {
        if (_bodies.Innermost is not { Call: { } call })
        {
            return;
        }

        var result = Values.ResultOf(call.Syntax);
        UniformityWrites.AddWritten(Nodes[node], result);
        Nodes[node].Events.Add(new UniformityEvent(UniformityEventKind.Bind, value, graph, _bodies.Opaque, result));
    }

    public void OnTopOperation(IOperation operation, ControlFlowGraph graph, int node) =>
        Nodes[node].Events.Add(new UniformityEvent(UniformityEventKind.Observe, operation, graph, _bodies.Opaque));

    // A by-value or ref parameter starts with the variance of its argument; an out parameter is written before it is
    // read
    private void BindArguments(IInvocationOperation call, ControlFlowGraph callGraph,
        ImmutableArray<IParameterSymbol> parameters, int node)
    {
        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter is not { RefKind: not RefKind.Out, Ordinal: var ordinal } ||
                ordinal >= parameters.Length)
            {
                continue;
            }

            Nodes[node].Events.Add(new UniformityEvent(UniformityEventKind.Bind, argument.Value, callGraph, null,
                Values.ValueOf(parameters[ordinal])));
        }
    }

    private void AddWrite(IOperation operation, ControlFlowGraph graph, int node)
    {
        UniformityWrites.AllocateTargets(Values, Nodes[node], operation);
        Nodes[node].Events.Add(new UniformityEvent(UniformityEventKind.Write, operation, graph, _bodies.Opaque));
    }

    private void Define(IParameterSymbol parameter, ThreadVariance variance, int node) =>
        Nodes[node].Events.Add(
            new UniformityEvent(UniformityEventKind.Define, null, null, null, Values.ValueOf(parameter), variance)
        );

    private static void RecordNode(Dictionary<SyntaxNode, ImmutableArray<int>.Builder> map, SyntaxNode syntax,
        int node)
    {
        if (!map.TryGetValue(syntax, out var nodes))
        {
            nodes = ImmutableArray.CreateBuilder<int>();
            map.Add(syntax, nodes);
        }

        if (!nodes.Contains(node))
        {
            nodes.Add(node);
        }
    }
}
