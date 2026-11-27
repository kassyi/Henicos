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

    // The parameters of each lambda and of the lambdas that enclose it, by its syntax
    private readonly Dictionary<SyntaxNode, ImmutableArray<IParameterSymbol>> _lambdaParameters = new();

    private UniformityValues? _values;

    private MemoryUniformity? _memory;

    public ImmutableArray<UniformityNode>.Builder Nodes { get; } = ImmutableArray.CreateBuilder<UniformityNode>();

    /// <summary>The nodes where a call or object creation is evaluated.</summary>
    public Dictionary<SyntaxNode, ImmutableArray<int>.Builder> NodesOf { get; } = new();

    /// <summary>The nodes where a call that splits the state machine is.</summary>
    public Dictionary<SyntaxNode, ImmutableArray<int>.Builder> SplitsAt { get; } = new();

    /// <summary>The barriers and barrier-reaching calls of the graph.</summary>
    public Dictionary<SyntaxNode, BarrierSite> Sites { get; } = new();

    /// <summary>Additional barrier-reaching calls when multiple calls share a single <see cref="SyntaxNode"/>.</summary>
    public Dictionary<SyntaxNode, ImmutableArray<RecordedBarrierCall>.Builder>? ExtraSiteCalls { get; private set; }

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

    /// <summary>
    /// The value index of the flow capture whose member a write names through <paramref name="reference"/> (the struct
    /// that an object initializer or a with expression fills).
    /// </summary>
    public bool TryGetCaptureRoot(IFlowCaptureReferenceOperation reference, out int index) =>
        Values.TryGetCaptureRoot(reference, out index);

    /// <summary>
    /// The locals and parameters that the capture <paramref name="reference"/> refers to by reference (the branches of
    /// a ref conditional).
    /// </summary>
    public ImmutableArray<ISymbol> GetCaptureRootAliases(IFlowCaptureReferenceOperation reference) =>
        Values.GetRootAliases(reference);

    /// <summary>Gets the captured property or indexer reference for <paramref name="id"/> in <paramref name="graph"/>, if any.</summary>
    public bool TryGetCapturedProperty(ControlFlowGraph graph, CaptureId id,
        [NotNullWhen(true)] out IPropertyReferenceOperation? property) =>
        Values.TryGetCapturedProperty(graph, id, out property);

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

        if (method.IsPrimaryConstructor())
        {
            var captured = ThreadVariance.Uniform;
            var hasCaptured = false;
            foreach (var member in StructAbiMembers.Get(method.ContainingType))
            {
                if (member.Kind == StructAbiMemberKind.CapturedParameter ||
                    (member is { Kind: StructAbiMemberKind.AutoProperty, Symbol: IPropertySymbol prop } &&
                     prop.TryGetDeclaringSyntax<ParameterSyntax>(out _)))
                {
                    hasCaptured = true;
                    var parameter = method.Parameters
                        .AsStackEnumerable()
                        .FirstOrDefault(member.Name, static (p, name) => p.Name == name);
                    if (parameter != null)
                    {
                        var bit = ArgumentMasks.Bit(parameter.Ordinal);
                        captured |= (masks.Block & bit) != 0 ? ThreadVariance.Block :
                            (masks.Grid & bit) != 0 ? ThreadVariance.Grid : ThreadVariance.Uniform;
                    }
                }
            }

            if (hasCaptured)
            {
                var thisIndex = Values.ValueOf(method.ContainingType);
                Nodes[node].Events.Add(
                    new UniformityEvent(UniformityEventKind.Define, null, null, null, thisIndex, captured));
            }
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
        if (capture.Value is IPropertyReferenceOperation property)
        {
            Values.RecordCapturedProperty(graph, capture.Id, property);
        }

        UniformityWrites.AddWritten(Nodes[node], ValueOf(graph, capture.Id));
        AddWrite(capture, graph, node);
        Memory.AddCapture(capture);
        if (capture.Syntax.Parent is RefExpressionSyntax &&
            UniformityWrites.GetRootSymbol(capture.Value) is { } referenced)
        {
            Values.AddCaptureAlias(graph, capture.Id, referenced);
        }
    }

    public void OnObjectCreation(IObjectCreationOperation creation, int node) =>
        RecordNode(NodesOf, creation.Syntax, node);

    public void OnCall(IInvocationOperation invocation, int node)
    {
        RecordNode(NodesOf, invocation.Syntax, node);
        Memory.AddCall(invocation, node);
    }

    // A call that splits the state machine; a barrier, or a call that reaches one, is also a site of GSH0037
    public void OnSplit(SplitPoint split, int node)
    {
        if (split.Kind == SplitKind.Gap)
        {
            OnGap(node);
            return;
        }

        if (split is not { Syntax: { } call, Graph: { } graph })
        {
            return;
        }

        Nodes[node].HasSplit = true;
        var splitIndex = Nodes[node].Splits.Count;
        Nodes[node].Splits.Add(call);
        RecordNode(SplitsAt, call, node);
        if (split.BarrierScope is { } scope)
        {
            RecordBarrierSite(split, call, scope, graph, node, splitIndex);
        }
    }

    // A gap inside a Gpu.SyncGrid(body) is not a site: the call is (a grid barrier). Every thread of the grid waits at
    // the grid sync that the expansion places in the gap, so it is a point where the threads that skipped a divergent
    // barrier wait while the others wait at that barrier (#982), and the memory is synced there, not where the body
    // starts.
    private void OnGap(int node)
    {
        Nodes[node].HasSplit = true;
        Memory.AddSync(SyncScope.Grid, node);
    }

    // A barrier or barrier-reaching call: a site of GSH0083 / GSH0084; a barrier itself syncs the memory, the callee
    // of a barrier-reaching call lowers it through its summary (AddCall)
    private void RecordBarrierSite(SplitPoint split, SyntaxNode call, SyncScope scope, ControlFlowGraph graph,
        int node, int splitIndex)
    {
        if (Sites.TryGetValue(call, out var existing))
        {
            var mergedScope = existing.Scope == SyncScope.Grid || scope == SyncScope.Grid
                ? SyncScope.Grid
                : scope;
            if (existing.Call is null && split.BarrierCall is { } newCall)
            {
                Sites[call] = new BarrierSite(mergedScope, newCall, graph, node, splitIndex);
            }
            else
            {
                if (mergedScope != existing.Scope)
                {
                    Sites[call] = existing with { Scope = mergedScope };
                }

                if (split.BarrierCall is { } extraCall)
                {
                    ExtraSiteCalls ??= new Dictionary<SyntaxNode, ImmutableArray<RecordedBarrierCall>.Builder>();
                    if (!ExtraSiteCalls.TryGetValue(call, out var extras))
                    {
                        extras = ImmutableArray.CreateBuilder<RecordedBarrierCall>();
                        ExtraSiteCalls.Add(call, extras);
                    }

                    extras.Add(new RecordedBarrierCall(extraCall, node, splitIndex));
                }
            }
        }
        else
        {
            Sites[call] = new BarrierSite(scope, split.BarrierCall, graph, node, splitIndex);
        }

        if (split.BarrierCall is { } callSite)
        {
            Nodes[node].Events.Add(new UniformityEvent(UniformityEventKind.Site, callSite.Operation, graph,
                _bodies.Opaque, Site: call));
        }
        else if (split.Kind == SplitKind.Call)
        {
            Memory.AddSync(scope, node);
        }
    }

    // M(ref x), M(out y), and s.M() of a struct member that may write s: written when the call returns
    public void OnCallReturned(IInvocationOperation invocation, ControlFlowGraph graph, int node)
    {
        if (UniformityWrites.WritesReceiver(invocation))
        {
            AddWrite(invocation, graph, node);
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                AddWrite(invocation, graph, node);
                return;
            }
        }
    }

    public void OnObjectCreationReturned(IObjectCreationOperation creation, ControlFlowGraph graph, int node)
    {
        foreach (var argument in creation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                AddWrite(creation, graph, node);
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
        if (body.Kind == SplicedBodyKind.Lambda)
        {
            _lambdaParameters[body.Syntax] = _bodies.LambdaParameters;
        }
    }

    public void OnBodyExited(SplicedBody body, int node) => _bodies.Pop(body);

    // return <value> of a local function body spliced where it is called: the result of that call; of a lambda: the
    // result of the lambda, which a call it is passed to reads as its operand
    public void OnBodyReturn(IOperation value, ControlFlowGraph graph, int node)
    {
        switch (_bodies.Innermost)
        {
            case { Call: { } call }:
                var result = Values.ResultOf(call.Syntax);
                UniformityWrites.AddWritten(Nodes[node], result);
                Nodes[node].Events.Add(
                    new UniformityEvent(UniformityEventKind.Bind, value, graph, _bodies.Opaque, result)
                );
                break;
            case { Kind: SplicedBodyKind.Lambda } lambda:
                var lambdaResult = Values.ResultOf(lambda.Syntax);
                UniformityWrites.AddWritten(Nodes[node], lambdaResult);
                Nodes[node].Events.Add(
                    new UniformityEvent(UniformityEventKind.BindLambdaResult, value, graph, lambda.Syntax, lambdaResult)
                );
                break;
        }
    }

    /// <summary>
    /// The parameters of the lambda, and of the lambdas that enclose it, that its result and the locals its body
    /// declares read as uniform (<see cref="UniformityEventKind.BindLambdaResult"/>).
    /// </summary>
    public ImmutableArray<IParameterSymbol> GetLambdaParameters(SyntaxNode lambda) =>
        _lambdaParameters.TryGetValue(lambda, out var parameters) ? parameters : ImmutableArray<IParameterSymbol>.Empty;

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
        UniformityWrites.AllocateTargets(Values, Nodes[node], operation, graph);
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
