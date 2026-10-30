using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Records, for <see cref="UniformityAnalysis"/>, what the operations of each node of a <see cref="SplicedFlowGraph"/>
/// write and evaluate, where the parameters get their variance, which calls and object creations each node evaluates,
/// and where the barriers and barrier-reaching calls are.
/// </summary>
/// <remarks>
/// A spliced lambda or local function body is opaque (<see cref="SplicedBody.IsOpaque"/>): its parameters are
/// thread-variant, as its callers are not followed, and its events carry the body, as an assignment in it to a local
/// declared outside it only adds to the variance. The index of a <c>Gpu.ForUnroll</c> body counts the same in every
/// thread.
/// </remarks>
/// <param name="method">The analyzed method.</param>
/// <param name="masks">Which parameters of <paramref name="method"/> receive a thread-variant argument.</param>
internal sealed class UniformityRecorder(IMethodSymbol method, ArgumentMasks masks) : IFlowRecorder
{
    private readonly Dictionary<ISymbol, int> _symbols = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<(ControlFlowGraph Graph, CaptureId Id), int> _captures = new();

    // The bodies of the lambdas and local functions being built that may run at any time
    private readonly ImmutableArray<SyntaxNode>.Builder _opaque = ImmutableArray.CreateBuilder<SyntaxNode>();

    private MemoryUniformity? _memory;

    public ImmutableArray<UniformityNode>.Builder Nodes { get; } = ImmutableArray.CreateBuilder<UniformityNode>();

    /// <summary>The nodes where a call or object creation is evaluated.</summary>
    public Dictionary<SyntaxNode, ImmutableArray<int>.Builder> NodesOf { get; } = new();

    /// <summary>The nodes where a call that splits the state machine is.</summary>
    public Dictionary<SyntaxNode, ImmutableArray<int>.Builder> SplitsAt { get; } = new();

    /// <summary>The barriers and barrier-reaching calls of the graph.</summary>
    public Dictionary<SyntaxNode, BarrierSite> Sites { get; } = new();

    /// <summary>The memory that the threads share, whose values follow the locals, parameters and flow captures.</summary>
    public MemoryUniformity Memory => _memory ??= new MemoryUniformity(this);

    /// <summary>The number of values: locals, parameters, flow captures and memory.</summary>
    public int ValueCount => _symbols.Count + _captures.Count + Memory.Count;

    /// <summary>The value index of a local or parameter, when the graph writes it.</summary>
    public bool TryGetValue(ISymbol symbol, out int index) => _symbols.TryGetValue(symbol, out index);

    /// <summary>The value index of a flow capture, when the graph writes it.</summary>
    public bool TryGetCapture(ControlFlowGraph graph, CaptureId id, out int index) =>
        _captures.TryGetValue((graph, id), out index);

    /// <summary>The value index of a flow capture, given one on first use.</summary>
    public int ValueOf(ControlFlowGraph graph, CaptureId id)
    {
        if (!_captures.TryGetValue((graph, id), out var index))
        {
            index = ValueCount;
            _captures.Add((graph, id), index);
        }

        return index;
    }

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

    public void BeforeAssignmentValue(IAssignmentOperation assignment, int node)
    {
    }

    public void OnAssignment(IAssignmentOperation assignment, ControlFlowGraph graph, int node)
    {
        AddWrite(assignment, graph, node);
        Memory.AddWrite(assignment.Target, node);
    }

    public void OnIncrement(IIncrementOrDecrementOperation increment, ControlFlowGraph graph, int node)
    {
        AddWrite(increment, graph, node);
        Memory.AddWrite(increment.Target, node);
    }

    public void OnFlowCapture(IFlowCaptureOperation capture, ControlFlowGraph graph, int node)
    {
        AddWritten(ValueOf(graph, capture.Id), node);
        AddWrite(capture, graph, node);
    }

    public void OnObjectCreation(IObjectCreationOperation creation, int node) => RecordNode(NodesOf, creation.Syntax, node);

    public void OnCall(IInvocationOperation invocation, int node)
    {
        RecordNode(NodesOf, invocation.Syntax, node);
        Memory.AddCall(invocation, node);
    }

    // A call that splits the state machine; a barrier, or a call that reaches one, is also a site of GSH0037. The gaps
    // inside a Gpu.SyncGrid(body) are not split points here: the call is (a grid barrier).
    public void OnSplit(SplitPoint split, int node)
    {
        if (split is not { Kind: not SplitKind.Gap, Syntax: { } call, Graph: { } graph })
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
            Nodes[node].Events.Add(new UniformityEvent(UniformityEventKind.Site, invocation, graph, Opaque));
        }
        else
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

    // The parameters of a lambda or local function are thread-variant, except the index of a ForUnroll body
    public void OnBodyEntered(SplicedBody body, int node)
    {
        var parameters = body.Parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            var isIndex = body.HasUniformIndex && i == parameters.Length - 1;
            Define(parameters[i], isIndex ? ThreadVariance.Uniform : ThreadVariance.Block, node);
        }

        if (body.IsOpaque)
        {
            _opaque.Add(body.Syntax);
        }
    }

    public void OnBodyExited(SplicedBody body, int node)
    {
        if (body.IsOpaque)
        {
            _opaque.RemoveAt(_opaque.Count - 1);
        }
    }

    public void OnTopOperation(IOperation operation, ControlFlowGraph graph, int node) =>
        Nodes[node].Events.Add(new UniformityEvent(UniformityEventKind.Observe, operation, graph, Opaque));

    private SyntaxNode? Opaque => _opaque.Count > 0 ? _opaque[_opaque.Count - 1] : null;

    private void AddWrite(IOperation operation, ControlFlowGraph graph, int node)
    {
        AllocateTargets(operation, node);
        Nodes[node].Events.Add(new UniformityEvent(UniformityEventKind.Write, operation, graph, Opaque));
    }

    private void Define(IParameterSymbol parameter, ThreadVariance variance, int node) =>
        Nodes[node].Events.Add(new UniformityEvent(UniformityEventKind.Define, null, null, null, ValueOf(parameter), variance));

    private static void RecordNode(Dictionary<SyntaxNode, ImmutableArray<int>.Builder> map, SyntaxNode syntax, int node)
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

    // Gives the locals and parameters that an operation writes a value index before the graph is solved
    private void AllocateTargets(IOperation operation, int node)
    {
        switch (operation)
        {
            case IDeconstructionAssignmentOperation deconstruction: AllocateTargets(deconstruction.Target, node); break;
            case IAssignmentOperation assignment: AllocateTargets(assignment.Target, node); break;
            case IIncrementOrDecrementOperation increment: AllocateTargets(increment.Target, node); break;
            case IInvocationOperation invocation: AllocateArguments(invocation, node); break;
            case IDeclarationExpressionOperation declaration: AllocateTargets(declaration.Expression, node); break;
            case ITupleOperation tuple: AllocateElements(tuple, node); break;
            case ILocalReferenceOperation local: AddWritten(ValueOf(local.Local), node); break;
            case IParameterReferenceOperation parameter: AddWritten(ValueOf(parameter.Parameter), node); break;
            case IFieldReferenceOperation or IPropertyReferenceOperation when UniformitySolver.GetStructRoot(operation) is { } root:
                AddWritten(ValueOf(root), node); break;
        }
    }

    private void AllocateArguments(IInvocationOperation invocation, int node)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                AllocateTargets(argument.Value, node);
            }
        }
    }

    private void AllocateElements(ITupleOperation tuple, int node)
    {
        foreach (var element in tuple.Elements)
        {
            AllocateTargets(element, node);
        }
    }

    private void AddWritten(int value, int node)
    {
        if (!Nodes[node].Written.Contains(value))
        {
            Nodes[node].Written.Add(value);
        }
    }

    private int ValueOf(ISymbol symbol)
    {
        if (!_symbols.TryGetValue(symbol, out var index))
        {
            index = ValueCount;
            _symbols.Add(symbol, index);
        }

        return index;
    }
}
