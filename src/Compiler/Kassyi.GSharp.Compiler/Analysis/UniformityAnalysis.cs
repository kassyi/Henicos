using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>How far a value, or the choice of a branch, may differ between the threads that run it.</summary>
[Flags]
internal enum ThreadVariance : byte
{
    /// <summary>The same in every thread of the grid.</summary>
    Uniform = 0,

    /// <summary>The same in every thread of a block, but may differ between the blocks (<c>BlockIndex</c>).</summary>
    Grid = 1,

    /// <summary>May differ between the threads of a block, and so between the threads of the grid.</summary>
    Block = Grid | 2
}

/// <summary>
/// Whether only some of the threads that must reach a barrier reach it, and what the others do instead.
/// </summary>
internal enum BarrierDivergence
{
    /// <summary>Every thread that must reach the barrier reaches it.</summary>
    None,

    /// <summary>
    /// The threads that skip the barrier leave the method without reaching another point where the state machine
    /// splits.
    /// </summary>
    ExitOnly,

    /// <summary>
    /// The threads that skip the barrier reach another point where the state machine splits: a later barrier, or the
    /// barrier itself in the next iteration of a loop.
    /// </summary>
    Condition
}

/// <summary>
/// Decides, on the control flow graph of a kernel or device function body, which values and which branches may differ
/// between threads, which code only some threads reach, and whether a barrier is divergent (GSH0037, GSH0077; #446,
/// #447).
/// </summary>
/// <remarks>
/// <para>
/// Values: a forward data flow over the locals, parameters and flow captures, with one <see cref="ThreadVariance"/> per
/// value. A value is thread-variant when it depends on <c>ThreadIndex</c>, on an intrinsic whose result differs between
/// threads (<see cref="SyncIntrinsicClassifier"/>), on a device function whose result is thread-variant for the
/// variance of its arguments (analyzed in the same way, cached per argument mask), on another method called with a
/// thread-variant argument, or on a parameter of a lambda or local function, whose callers are not followed. It varies
/// between blocks only when it depends on <c>BlockIndex</c>. An assignment sets the variance of its target from its
/// value, and so also makes it uniform again (#447); a deconstruction assigns each element (#446-2). An assignment
/// inside a lambda or local function to a local declared outside it only adds to the variance, as the body may run at
/// any time.
/// </para>
/// <para>
/// Branches: a branch is divergent when its condition is thread-variant. The code that only some threads reach is its
/// influence region: what the successors of the branch reach before its immediate post-dominator (computed on the
/// reverse graph with a virtual exit, to which the end of the method, a <c>throw</c> and any part that cannot reach the
/// end are connected). A loop whose exit condition is thread-variant puts its whole body in the region. Where paths
/// from two successors of a divergent branch meet again (in its region or at its post-dominator, not coming back
/// through the branch), a value written in the region may differ between threads (LLVM's sync dependence; after a loop
/// with a thread-variant exit, its temporal divergence). A value written after an early <c>return</c> is not: the
/// threads that returned never meet the others. The values, the regions and the meeting points are solved together
/// until none grows.
/// </para>
/// <para>
/// Barriers: a barrier in the influence region of a divergent branch is <see cref="BarrierDivergence.Condition"/> when
/// the threads that skip it (a successor of the branch that does not reach the barrier without coming back through the
/// branch) can reach any point where the state machine splits, the barrier itself included; otherwise only on their
/// way out of the method, <see cref="BarrierDivergence.ExitOnly"/>. A block barrier is divergent only on a branch that
/// varies within a block; a grid barrier also on one that varies between blocks.
/// </para>
/// <para>
/// The bodies of <c>Gpu.ForUnroll</c> (a loop whose index is uniform) and <c>Gpu.SyncGrid(body)</c> are spliced into
/// the graph where they are called, as they are expanded. Any other lambda is spliced where it is created and a local
/// function where it is called, with their parameters thread-variant.
/// </para>
/// <para>
/// [WHY] Two assumptions keep the analysis within one method, and both may miss a divergent branch: a parameter of a
/// kernel or device function is uniform unless the caller's argument mask says otherwise, and an element read with a
/// uniform index is uniform (memory is not followed). Fields and array elements are not values of the analysis.
/// </para>
/// </remarks>
internal sealed partial class UniformityAnalysis
{
    private static readonly ConditionalWeakTable<Compilation,
            ConcurrentDictionary<IMethodSymbol, ConcurrentDictionary<ArgumentMasks, CalleeVariance>>>
        s_callees = new();

    private readonly SemanticModel _model;
    private readonly IMethodSymbol _method;
    private readonly HashSet<IMethodSymbol> _calleesInProgress;
    private readonly HashSet<IMethodSymbol> _localFunctionsInProgress = new(SymbolEqualityComparer.Default);

    private readonly ImmutableArray<FlowNode>.Builder _nodes = ImmutableArray.CreateBuilder<FlowNode>();
    private readonly Dictionary<ISymbol, int> _symbols = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<(ControlFlowGraph Graph, CaptureId Id), int> _captures = new();

    // The nodes where a call or object creation is evaluated, and where a call that splits the state machine is
    private readonly Dictionary<SyntaxNode, ImmutableArray<int>.Builder> _nodesOf = new();
    private readonly Dictionary<SyntaxNode, ImmutableArray<int>.Builder> _splitsAt = new();

    // The bodies of the lambdas and local functions being built that may run at any time
    private readonly ImmutableArray<SyntaxNode>.Builder _opaque = ImmutableArray.CreateBuilder<SyntaxNode>();

    private FlowNode _current;
    private int _exit;

    // The parameters of the analyzed method by value index, and those that receive the thread index itself
    private readonly Dictionary<int, IParameterSymbol> _parameters = new();
    private ulong _identityMask;

    // The solution: the variance of each value at the start of each node, the variance of the branches that decide
    // whether each node runs, the variance of each node's own branch, the immediate post-dominator of each node
    private ThreadVariance[][] _in = [];
    private ThreadVariance[] _region = [];
    private ThreadVariance[] _branch = [];
    private int[] _postDominator = [];
    private ImmutableArray<int>[] _successors = [];
    private ImmutableArray<int>?[] _regions = [];
    private ImmutableArray<int>?[] _joins = [];
    private int[] _predecessorCount = [];
    private Dictionary<SyntaxNode, ThreadVariance>? _observed;

    private UniformityAnalysis(SemanticModel model, IMethodSymbol method, HashSet<IMethodSymbol>? calleesInProgress)
    {
        _model = model;
        _method = method;
        _calleesInProgress = calleesInProgress ?? new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        _current = NewNode();
    }

    /// <summary>
    /// Analyzes <paramref name="graph"/>, the body of <paramref name="method"/>, with uniform parameters.
    /// </summary>
    public static UniformityAnalysis Analyze(ControlFlowGraph graph, SemanticModel model, IMethodSymbol method)
    {
        var analysis = new UniformityAnalysis(model, method, calleesInProgress: null);
        analysis.Run(graph, default);
        return analysis;
    }

    /// <summary>
    /// Whether only some threads of a block reach the call or object creation <paramref name="node"/>.
    /// </summary>
    public bool IsReachedByPartOfBlock(SyntaxNode node) =>
        _nodesOf.TryGetValue(node, out var nodes) &&
        nodes
            .AsStackEnumerable()
            .Any(_region, static (index, region) => region[index] == ThreadVariance.Block);

    /// <summary>
    /// Whether <paramref name="expression"/> may evaluate to different values in different threads of a block.
    /// </summary>
    public bool IsThreadVariant(SyntaxNode expression)
    {
        if (_observed == null)
        {
            Observe();
        }

        return _observed != null &&
               _observed.TryGetValue(expression, out var variance) &&
               variance == ThreadVariance.Block;
    }

    private static bool Diverges(ThreadVariance branch, SyncScope scope) =>
        scope == SyncScope.Grid ? branch != ThreadVariance.Uniform : branch == ThreadVariance.Block;

    private void Run(ControlFlowGraph graph, ArgumentMasks masks)
    {
        _identityMask = masks.Identity;
        foreach (var parameter in _method.Parameters)
        {
            var bit = ParameterBit(parameter.Ordinal);
            var variance = (masks.Block & bit) != 0 ? ThreadVariance.Block :
                (masks.Grid & bit) != 0 ? ThreadVariance.Grid : ThreadVariance.Uniform;
            _parameters[ValueOf(parameter)] = parameter;
            Define(parameter, variance);
        }

        var start = _current;
        var (entry, exit) = Build(graph, isRoot: true);
        Connect(start, entry);
        _exit = exit.Index;

        ComputePostDominators();
        Solve();
    }

    // Parameters past the 64th share the last bit
    private static ulong ParameterBit(int ordinal) => 1UL << Math.Min(ordinal, 63);

    // ---- Building the graph -------------------------------------------------------------------------------------

    // Builds the nodes of graph; returns the node of its entry block and the last node of its exit block
    private (FlowNode Entry, FlowNode Exit) Build(ControlFlowGraph graph, bool isRoot)
    {
        var blocks = graph.Blocks;
        var starts = new FlowNode[blocks.Length];
        var ends = new FlowNode[blocks.Length];
        for (var i = 0; i < blocks.Length; i++)
        {
            _current = NewNode();
            starts[i] = _current;
            foreach (var operation in blocks[i].Operations)
            {
                WalkTop(operation, graph);
            }

            var block = blocks[i];
            if (block.BranchValue is { } value)
            {
                WalkTop(value, graph);
                if (block.ConditionalSuccessor != null)
                {
                    _current.Condition = value;
                    _current.Graph = graph;
                }
                else if (isRoot && block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return)
                {
                    _current.ReturnValue = value;
                    _current.Graph = graph;
                }
            }

            ends[i] = _current;
        }

        // A branch with no destination (throw) leaves through the exit block
        var exitBlock = blocks.Length - 1;
        for (var i = 0; i < exitBlock; i++)
        {
            if (blocks[i].FallThroughSuccessor is { } fallThrough)
            {
                Connect(ends[i], starts[fallThrough.Destination?.Ordinal ?? exitBlock]);
            }

            if (blocks[i].ConditionalSuccessor is { } conditional)
            {
                var jump = starts[conditional.Destination?.Ordinal ?? exitBlock].Index;
                var fall = starts[blocks[i].FallThroughSuccessor?.Destination?.Ordinal ?? exitBlock].Index;
                var jumpsWhenTrue = blocks[i].ConditionKind == ControlFlowConditionKind.WhenTrue;
                Connect(ends[i], _nodes[jump]);
                ends[i].WhenTrue = jumpsWhenTrue ? jump : fall;
                ends[i].WhenFalse = jumpsWhenTrue ? fall : jump;
            }
        }

        return (starts[0], ends[exitBlock]);
    }

    private void WalkTop(IOperation operation, ControlFlowGraph graph)
    {
        Walk(operation, graph);
        _current.Events.Add(new FlowEvent(FlowEventKind.Observe, operation, graph, Opaque));
    }

    // The writes of an operation in the order C# evaluates it; lambdas and the bodies of inlined macros become nodes
    private void Walk(IOperation operation, ControlFlowGraph graph)
    {
        switch (operation)
        {
            case IInvocationOperation invocation:
                WalkInvocation(invocation, graph);
                return;
            case IFlowAnonymousFunctionOperation function:
                Splice(function, graph, isLoop: false, opaque: true, uniformIndex: false);
                return;
            case IObjectCreationOperation creation:
                RecordNode(_nodesOf, creation.Syntax);
                break;
            case IDeconstructionAssignmentOperation deconstruction:
                Walk(deconstruction.Value, graph);
                Walk(deconstruction.Target, graph);
                AddWrite(deconstruction, graph);
                return;
            case IAssignmentOperation assignment:
                Walk(assignment.Value, graph);
                Walk(assignment.Target, graph);
                AddWrite(assignment, graph);
                return;
            case IIncrementOrDecrementOperation increment:
                Walk(increment.Target, graph);
                AddWrite(increment, graph);
                return;
            case IFlowCaptureOperation capture:
                Walk(capture.Value, graph);
                AddWritten(ValueOf(graph, capture.Id));
                AddWrite(capture, graph);
                return;
        }

        foreach (var child in operation.ChildOperations)
        {
            Walk(child, graph);
        }
    }

    private void WalkInvocation(IInvocationOperation invocation, ControlFlowGraph graph)
    {
        var method = invocation.TargetMethod;
        var macro = GpuMacroClassifier.Classify(method);
        if (macro is { Kind: GpuMacroKind.ForUnroll or GpuMacroKind.SyncGrid, HasBody: true })
        {
            WalkMacro(invocation, macro, graph);
            return;
        }

        if (invocation.Instance is { } instance)
        {
            Walk(instance, graph);
        }

        var writesArgument = false;
        foreach (var argument in invocation.Arguments)
        {
            Walk(argument.Value, graph);
            writesArgument |= argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out;
        }

        if (method.MethodKind == MethodKind.LocalFunction)
        {
            SpliceLocalFunction(method.OriginalDefinition, graph);
        }

        RecordNode(_nodesOf, invocation.Syntax);
        var intrinsic = SyncIntrinsicClassifier.Classify(method);
        if (intrinsic.Kind is SyncIntrinsicKind.BlockBarrier or SyncIntrinsicKind.GridBarrier)
        {
            AddSplit(invocation.Syntax, new BarrierSite(intrinsic.Scope, Call: null, graph));
        }
        else if (intrinsic.SplitsStateMachine)
        {
            AddSplit(invocation.Syntax, site: null);
        }
        else if (SuspensionAnalysis.IsSuspendingFunction(method, _model.Compilation))
        {
            AddSplit(invocation.Syntax,
                SuspensionAnalysis.ReachesBlockBarrier(method, _model.Compilation)
                    ? new BarrierSite(SyncScope.Block, invocation, graph)
                    : null);
        }

        if (writesArgument)
        {
            AddWrite(invocation, graph);
        }
    }

    // <arguments> <body>: the body where it is called, once for SyncGrid, in a loop for ForUnroll; a SyncGrid body with
    // a gap is a grid barrier
    private void WalkMacro(IInvocationOperation invocation, GpuMacroCall macro, ControlFlowGraph graph)
    {
        IFlowAnonymousFunctionOperation? body = null;
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == macro.LambdaArgumentIndex && UnwrapLambda(argument.Value) is { } lambda)
            {
                body = lambda;
                continue;
            }

            Walk(argument.Value, graph);
        }

        RecordNode(_nodesOf, invocation.Syntax);
        if (macro.Kind == GpuMacroKind.SyncGrid && body?.Syntax is LambdaExpressionSyntax syntax && HasGap(syntax))
        {
            AddSplit(invocation.Syntax, new BarrierSite(SyncScope.Grid, Call: null, graph));
        }

        if (body != null)
        {
            Splice(body, graph, isLoop: macro.Kind == GpuMacroKind.ForUnroll, opaque: false,
                uniformIndex: macro.Kind == GpuMacroKind.ForUnroll);
        }
    }

    private bool HasGap(LambdaExpressionSyntax body)
    {
        var elements = SyncGridBody.Flatten(body, _model).Elements;
        for (var i = 0; i < elements.Length; i++)
        {
            if (SyncGridBody.IsSyncPoint(elements, i, _model))
            {
                return true;
            }
        }

        return false;
    }

    private static IFlowAnonymousFunctionOperation? UnwrapLambda(IOperation operation) => operation switch
    {
        IFlowAnonymousFunctionOperation lambda => lambda,
        IDelegateCreationOperation creation => UnwrapLambda(creation.Target),
        IConversionOperation conversion => UnwrapLambda(conversion.Operand),
        _ => null
    };

    // The body of a lambda where the graph reaches it: its parameters are thread-variant, except the index of a
    // ForUnroll body, which counts the same in every thread
    private void Splice(IFlowAnonymousFunctionOperation function, ControlFlowGraph graph, bool isLoop, bool opaque,
        bool uniformIndex)
    {
        var parameters = function.Symbol.Parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            var isIndex = uniformIndex && i == parameters.Length - 1;
            Define(parameters[i], isIndex ? ThreadVariance.Uniform : ThreadVariance.Block);
        }

        var body = graph.GetAnonymousFunctionControlFlowGraphInScope(function);
        SpliceGraph(body, opaque ? function.Syntax : null, isLoop);
    }

    // The body of a local function where it is called, once on each path that calls it (a recursive call adds nothing)
    private void SpliceLocalFunction(IMethodSymbol function, ControlFlowGraph graph)
    {
        if (!function.TryGetDeclaringSyntax(out LocalFunctionStatementSyntax? syntax) ||
            !_localFunctionsInProgress.Add(function))
        {
            return;
        }

        foreach (var parameter in function.Parameters)
        {
            Define(parameter, ThreadVariance.Block);
        }

        SpliceGraph(graph.GetLocalFunctionControlFlowGraphInScope(function), syntax, isLoop: false);
        _localFunctionsInProgress.Remove(function);
    }

    private void SpliceGraph(ControlFlowGraph body, SyntaxNode? opaque, bool isLoop)
    {
        var before = _current;
        if (opaque != null)
        {
            _opaque.Add(opaque);
        }

        var (entry, exit) = Build(body, isRoot: false);
        if (opaque != null)
        {
            _opaque.RemoveAt(_opaque.Count - 1);
        }

        var after = NewNode();
        Connect(before, entry);
        Connect(exit, after);
        if (isLoop)
        {
            Connect(exit, entry);
        }

        _current = after;
    }

    private SyntaxNode? Opaque => _opaque.Count > 0 ? _opaque[_opaque.Count - 1] : null;

    private void AddWrite(IOperation operation, ControlFlowGraph graph)
    {
        AllocateTargets(operation);
        _current.Events.Add(new FlowEvent(FlowEventKind.Write, operation, graph, Opaque));
    }

    // A call that splits the state machine; a barrier, or a call that reaches one, is also a site of GSH0037
    private void AddSplit(SyntaxNode call, BarrierSite? site)
    {
        _current.HasSplit = true;
        _current.Splits.Add(call);
        RecordNode(_splitsAt, call);
        if (site is not { } barrier)
        {
            return;
        }

        _sites[call] = barrier;
        if (barrier.Call is { } invocation)
        {
            _current.Events.Add(new FlowEvent(FlowEventKind.Site, invocation, barrier.Graph, Opaque));
        }
    }

    private void Define(IParameterSymbol parameter, ThreadVariance variance) =>
        _current.Events.Add(new FlowEvent(FlowEventKind.Define, null, null, null, ValueOf(parameter), variance));

    private void RecordNode(Dictionary<SyntaxNode, ImmutableArray<int>.Builder> map, SyntaxNode syntax)
    {
        if (!map.TryGetValue(syntax, out var nodes))
        {
            nodes = ImmutableArray.CreateBuilder<int>();
            map.Add(syntax, nodes);
        }

        if (!nodes.Contains(_current.Index))
        {
            nodes.Add(_current.Index);
        }
    }

    // Gives the locals and parameters that an operation writes a value index before the graph is solved
    private void AllocateTargets(IOperation operation)
    {
        switch (operation)
        {
            case IDeconstructionAssignmentOperation deconstruction:
                AllocateTargets(deconstruction.Target);
                break;
            case IAssignmentOperation assignment:
                AllocateTargets(assignment.Target);
                break;
            case IIncrementOrDecrementOperation increment:
                AllocateTargets(increment.Target);
                break;
            case IInvocationOperation invocation:
                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
                    {
                        AllocateTargets(argument.Value);
                    }
                }

                break;
            case IDeclarationExpressionOperation declaration:
                AllocateTargets(declaration.Expression);
                break;
            case ITupleOperation tuple:
                foreach (var element in tuple.Elements)
                {
                    AllocateTargets(element);
                }

                break;
            case ILocalReferenceOperation local:
                AddWritten(ValueOf(local.Local));
                break;
            case IParameterReferenceOperation parameter:
                AddWritten(ValueOf(parameter.Parameter));
                break;
            case IFieldReferenceOperation or IPropertyReferenceOperation when GetStructRoot(operation) is { } root:
                AddWritten(ValueOf(root));
                break;
        }
    }

    private void AddWritten(int value)
    {
        if (!_current.Written.Contains(value))
        {
            _current.Written.Add(value);
        }
    }

    private int ValueOf(ISymbol symbol)
    {
        if (!_symbols.TryGetValue(symbol, out var index))
        {
            index = _symbols.Count + _captures.Count;
            _symbols.Add(symbol, index);
        }

        return index;
    }

    private int ValueOf(ControlFlowGraph graph, CaptureId id)
    {
        if (!_captures.TryGetValue((graph, id), out var index))
        {
            index = _symbols.Count + _captures.Count;
            _captures.Add((graph, id), index);
        }

        return index;
    }

    private FlowNode NewNode()
    {
        var node = new FlowNode(_nodes.Count);
        _nodes.Add(node);
        return node;
    }

    private static void Connect(FlowNode from, FlowNode to)
    {
        if (!from.Successors.Contains(to.Index))
        {
            from.Successors.Add(to.Index);
        }
    }

    // ---- Post-dominators ----------------------------------------------------------------------------------------

    // The immediate post-dominators on the reverse graph from a virtual exit (Cooper, Harvey and Kennedy), which the
    // end of the method and every node that cannot reach it lead to
    private void ComputePostDominators()
    {
        var count = _nodes.Count;
        var virtualExit = count;
        var successors = new ImmutableArray<int>.Builder[count + 1];
        for (var i = 0; i < count; i++)
        {
            successors[i] = ImmutableArray.CreateBuilder<int>();
            successors[i].AddRange(_nodes[i].Successors);
        }

        successors[virtualExit] = ImmutableArray.CreateBuilder<int>();
        successors[_exit].Add(virtualExit);

        var predecessors = Predecessors(successors);
        var reaches = new bool[count + 1];
        MarkReverseReachable(virtualExit, predecessors, reaches);
        for (var i = 0; i < count; i++)
        {
            if (!reaches[i])
            {
                successors[i].Add(virtualExit);
            }
        }

        predecessors = Predecessors(successors);
        var order = ReversePostOrder(virtualExit, predecessors, out var position);

        var postDominator = new int[count + 1];
        for (var i = 0; i < postDominator.Length; i++)
        {
            postDominator[i] = -1;
        }

        postDominator[virtualExit] = virtualExit;
        bool changed;
        do
        {
            changed = false;
            foreach (var node in order)
            {
                if (node == virtualExit)
                {
                    continue;
                }

                var candidate = -1;
                foreach (var successor in successors[node])
                {
                    if (postDominator[successor] < 0)
                    {
                        continue;
                    }

                    candidate = candidate < 0 ? successor : Intersect(successor, candidate, postDominator, position);
                }

                if (candidate >= 0 && postDominator[node] != candidate)
                {
                    postDominator[node] = candidate;
                    changed = true;
                }
            }
        } while (changed);

        _postDominator = postDominator;
        _successors = new ImmutableArray<int>[count];
        _predecessorCount = new int[count];
        for (var i = 0; i < count; i++)
        {
            _successors[i] = _nodes[i].Successors.ToImmutable();
            foreach (var successor in _successors[i])
            {
                _predecessorCount[successor]++;
            }
        }
    }

    private static ImmutableArray<int>.Builder[] Predecessors(ImmutableArray<int>.Builder[] successors)
    {
        var predecessors = new ImmutableArray<int>.Builder[successors.Length];
        for (var i = 0; i < predecessors.Length; i++)
        {
            predecessors[i] = ImmutableArray.CreateBuilder<int>();
        }

        for (var i = 0; i < successors.Length; i++)
        {
            foreach (var successor in successors[i])
            {
                predecessors[successor].Add(i);
            }
        }

        return predecessors;
    }

    private static void MarkReverseReachable(int start, ImmutableArray<int>.Builder[] predecessors, bool[] reached)
    {
        var stack = ImmutableArray.CreateBuilder<int>();
        stack.Add(start);
        reached[start] = true;
        while (stack.Count > 0)
        {
            var node = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            foreach (var predecessor in predecessors[node])
            {
                if (!reached[predecessor])
                {
                    reached[predecessor] = true;
                    stack.Add(predecessor);
                }
            }
        }
    }

    // The nodes in reverse post-order of a depth-first walk of the reverse graph, and the post-order number of each
    private static ImmutableArray<int> ReversePostOrder(int start, ImmutableArray<int>.Builder[] predecessors,
        out int[] position)
    {
        position = new int[predecessors.Length];
        var visited = new bool[predecessors.Length];
        var postOrder = ImmutableArray.CreateBuilder<int>(predecessors.Length);
        var stack = ImmutableArray.CreateBuilder<(int Node, int Next)>();
        stack.Add((start, 0));
        visited[start] = true;
        while (stack.Count > 0)
        {
            var (node, next) = stack[stack.Count - 1];
            if (next < predecessors[node].Count)
            {
                stack[stack.Count - 1] = (node, next + 1);
                var predecessor = predecessors[node][next];
                if (!visited[predecessor])
                {
                    visited[predecessor] = true;
                    stack.Add((predecessor, 0));
                }

                continue;
            }

            stack.RemoveAt(stack.Count - 1);
            position[node] = postOrder.Count;
            postOrder.Add(node);
        }

        postOrder.Reverse();
        return postOrder.ToImmutable();
    }

    private static int Intersect(int a, int b, int[] postDominator, int[] position)
    {
        while (a != b)
        {
            while (position[a] < position[b])
            {
                a = postDominator[a];
            }

            while (position[b] < position[a])
            {
                b = postDominator[b];
            }
        }

        return a;
    }

    // The influence region of a branch: the nodes its successors reach before its immediate post-dominator
    private ImmutableArray<int> GetRegion(int branch)
    {
        if (_regions[branch] is { } cached)
        {
            return cached;
        }

        var stop = _postDominator[branch];
        var reached = new bool[_nodes.Count];
        var region = ImmutableArray.CreateBuilder<int>();
        var stack = ImmutableArray.CreateBuilder<int>();
        foreach (var successor in _successors[branch])
        {
            if (successor != stop && !reached[successor])
            {
                reached[successor] = true;
                stack.Add(successor);
            }
        }

        while (stack.Count > 0)
        {
            var node = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            region.Add(node);
            foreach (var successor in _successors[node])
            {
                if (successor != stop && !reached[successor])
                {
                    reached[successor] = true;
                    stack.Add(successor);
                }
            }
        }

        var result = region.ToImmutable();
        _regions[branch] = result;
        return result;
    }

    // The meeting points of a branch: the nodes with several predecessors, up to and including its immediate
    // post-dominator, that two of its successors reach without coming back through the branch
    private ImmutableArray<int> GetJoins(int branch)
    {
        if (_joins[branch] is { } cached)
        {
            return cached;
        }

        var stop = _postDominator[branch];
        var reachedBy = new int[_nodes.Count];
        var meets = new bool[_nodes.Count];
        for (var i = 0; i < reachedBy.Length; i++)
        {
            reachedBy[i] = -1;
        }

        var successors = _successors[branch];
        var reached = new bool[_nodes.Count];
        var stack = ImmutableArray.CreateBuilder<int>();
        for (var path = 0; path < successors.Length; path++)
        {
            Array.Clear(reached, 0, reached.Length);
            stack.Add(successors[path]);
            while (stack.Count > 0)
            {
                var node = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (node == branch || reached[node])
                {
                    continue;
                }

                reached[node] = true;
                if (reachedBy[node] < 0)
                {
                    reachedBy[node] = path;
                }
                else if (reachedBy[node] != path)
                {
                    meets[node] = true;
                }

                if (node == stop)
                {
                    continue;
                }

                foreach (var successor in _successors[node])
                {
                    stack.Add(successor);
                }
            }
        }

        var joins = ImmutableArray.CreateBuilder<int>();
        for (var node = 0; node < meets.Length; node++)
        {
            if (meets[node] && _predecessorCount[node] >= 2)
            {
                joins.Add(node);
            }
        }

        // [WHY] A branch that its own region comes back to is the exit test of a loop: the threads leave the loop in
        // different iterations, so what the loop writes differs after it (LLVM's cycle exit divergence,
        // propagateCycleExitDivergence). The walk above stops at the branch and never sees the exit meet the body.
        if (stop < _nodes.Count && GetRegion(branch).Contains(branch) && !joins.Contains(stop))
        {
            joins.Add(stop);
        }

        var result = joins.ToImmutable();
        _joins[branch] = result;
        return result;
    }

    // ---- Barriers -----------------------------------------------------------------------------------------------

    // A barrier at node in the influence region of branch: Condition when a successor of the branch that does not reach
    // the barrier (without coming back through the branch) can reach a split point, the barrier itself included
    private BarrierDivergence Classify(int branch, int node)
    {
        var skips = false;
        foreach (var successor in _successors[branch])
        {
            if (Reaches(successor, node, avoid: branch))
            {
                continue;
            }

            skips = true;
            if (ReachesSplit(successor))
            {
                return BarrierDivergence.Condition;
            }
        }

        // [WHY] A barrier in the region that every successor reaches can only come from a graph that the structured
        // statements of C# do not build; it is not taken for an exit
        return skips ? BarrierDivergence.ExitOnly : BarrierDivergence.Condition;
    }

    private bool Reaches(int from, int target, int avoid)
    {
        var reached = new bool[_nodes.Count];
        var stack = ImmutableArray.CreateBuilder<int>();
        stack.Add(from);
        reached[from] = true;
        while (stack.Count > 0)
        {
            var node = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            if (node == target)
            {
                return true;
            }

            foreach (var successor in _successors[node])
            {
                if (successor != avoid && !reached[successor])
                {
                    reached[successor] = true;
                    stack.Add(successor);
                }
            }
        }

        return false;
    }

    private bool ReachesSplit(int from)
    {
        var reached = new bool[_nodes.Count];
        var stack = ImmutableArray.CreateBuilder<int>();
        stack.Add(from);
        reached[from] = true;
        while (stack.Count > 0)
        {
            var node = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            if (_nodes[node].HasSplit)
            {
                return true;
            }

            foreach (var successor in _successors[node])
            {
                if (!reached[successor])
                {
                    reached[successor] = true;
                    stack.Add(successor);
                }
            }
        }

        return false;
    }

    // ---- Solving ------------------------------------------------------------------------------------------------

    // The forward data flow and the influence regions, alternately until the regions stop growing; both only grow
    private void Solve()
    {
        var count = _nodes.Count;
        var values = _symbols.Count + _captures.Count;
        _in = new ThreadVariance[count][];
        for (var i = 0; i < count; i++)
        {
            _in[i] = new ThreadVariance[values];
        }

        _region = new ThreadVariance[count];
        _branch = new ThreadVariance[count];
        _regions = new ImmutableArray<int>?[count];
        _joins = new ImmutableArray<int>?[count];

        var state = new ThreadVariance[values];
        do
        {
            Propagate(state);
        } while (UpdateRegions(state));
    }

    private void Propagate(ThreadVariance[] state)
    {
        bool changed;
        do
        {
            changed = false;
            for (var node = 0; node < _nodes.Count; node++)
            {
                RunNode(node, state);
                foreach (var successor in _successors[node])
                {
                    var target = _in[successor];
                    for (var value = 0; value < state.Length; value++)
                    {
                        var merged = target[value] | state[value];
                        if (merged != target[value])
                        {
                            target[value] = merged;
                            changed = true;
                        }
                    }
                }
            }
        } while (changed);
    }

    // The variance of each branch at the end of its node; adds the variance of a branch to the nodes of its region
    private bool UpdateRegions(ThreadVariance[] state)
    {
        var changed = false;
        for (var node = 0; node < _nodes.Count; node++)
        {
            if (_nodes[node] is not { Condition: { } condition, Graph: { } graph } || _successors[node].Length < 2)
            {
                continue;
            }

            RunNode(node, state);
            var variance = _branch[node] | Evaluate(condition, graph, state);
            if (variance == _branch[node])
            {
                continue;
            }

            _branch[node] = variance;
            var region = GetRegion(node);
            foreach (var member in region)
            {
                var merged = _region[member] | variance;
                if (merged != _region[member])
                {
                    _region[member] = merged;
                    changed = true;
                }
            }

            // A value written in the region may differ where the paths of the branch meet again
            foreach (var join in GetJoins(node))
            {
                var target = _in[join];
                foreach (var member in region)
                {
                    foreach (var value in _nodes[member].Written)
                    {
                        var merged = target[value] | variance;
                        if (merged != target[value])
                        {
                            target[value] = merged;
                            changed = true;
                        }
                    }
                }
            }
        }

        return changed;
    }

    // The variance of every value at the end of node, into state
    private void RunNode(int node, ThreadVariance[] state)
    {
        _in[node].AsSpan().CopyTo(state);
        foreach (var flowEvent in _nodes[node].Events)
        {
            switch (flowEvent.Kind)
            {
                case FlowEventKind.Define:
                    state[flowEvent.Value] = flowEvent.Variance;
                    break;
                case FlowEventKind.Write when flowEvent is { Operation: { } operation, Graph: { } graph }:
                    ApplyWrite(operation, graph, state, flowEvent.Opaque);
                    break;
                case FlowEventKind.Observe when _observed != null &&
                                                flowEvent is { Operation: { } observed, Graph: { } observedGraph }:
                    Evaluate(observed, observedGraph, state);
                    break;
                case FlowEventKind.Site when _siteMasks != null &&
                                             flowEvent is
                                             {
                                                 Operation: IInvocationOperation call,
                                                 Graph: { } callGraph
                                             }:
                    RecordSiteMasks(call, callGraph, state);
                    break;
            }
        }
    }

    // Records the variance of every expression the graph evaluates, from the solved state
    private void Observe()
    {
        _observed = new Dictionary<SyntaxNode, ThreadVariance>();
        var state = new ThreadVariance[_symbols.Count + _captures.Count];
        for (var node = 0; node < _nodes.Count; node++)
        {
            RunNode(node, state);
            if (_nodes[node] is { Condition: { } condition, Graph: { } graph })
            {
                Evaluate(condition, graph, state);
            }
        }
    }

    private void ApplyWrite(IOperation operation, ControlFlowGraph graph, ThreadVariance[] state, SyntaxNode? opaque)
    {
        switch (operation)
        {
            case IFlowCaptureOperation capture:
                state[ValueOf(graph, capture.Id)] = Evaluate(capture.Value, graph, state);
                break;
            case IDeconstructionAssignmentOperation deconstruction:
                Deconstruct(deconstruction.Target, deconstruction.Value, graph, state, opaque);
                break;
            case ISimpleAssignmentOperation assignment:
                // Assigning through a ref local writes the variable it refers to, which is not followed
                var rebinds = assignment.IsRef ||
                              assignment.Target is not ILocalReferenceOperation { Local.IsRef: true };
                Assign(assignment.Target, Evaluate(assignment.Value, graph, state), rebinds, state, opaque);
                break;
            case ICompoundAssignmentOperation compound:
                Assign(compound.Target,
                    Evaluate(compound.Target, graph, state) | Evaluate(compound.Value, graph, state),
                    strong: true, state, opaque);
                break;
            case IIncrementOrDecrementOperation increment:
                Assign(increment.Target, Evaluate(increment.Target, graph, state), strong: true, state, opaque);
                break;
            case IInvocationOperation invocation:
                WriteArguments(invocation, graph, state, opaque);
                break;
        }
    }

    // (a, b) = (x, y): each element from its own value when both sides are tuples, from the whole value otherwise
    private void Deconstruct(IOperation target, IOperation value, ControlFlowGraph graph, ThreadVariance[] state,
        SyntaxNode? opaque)
    {
        target = target is IDeclarationExpressionOperation declaration ? declaration.Expression : target;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        if (target is not ITupleOperation targets)
        {
            Assign(target, Evaluate(value, graph, state), strong: true, state, opaque);
            return;
        }

        if (value is ITupleOperation values && values.Elements.Length == targets.Elements.Length)
        {
            for (var i = 0; i < targets.Elements.Length; i++)
            {
                Deconstruct(targets.Elements[i], values.Elements[i], graph, state, opaque);
            }

            return;
        }

        var variance = Evaluate(value, graph, state);
        foreach (var element in targets.Elements)
        {
            AssignAll(element, variance, state, opaque);
        }
    }

    private void AssignAll(IOperation target, ThreadVariance variance, ThreadVariance[] state, SyntaxNode? opaque)
    {
        target = target is IDeclarationExpressionOperation declaration ? declaration.Expression : target;
        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                AssignAll(element, variance, state, opaque);
            }

            return;
        }

        Assign(target, variance, strong: true, state, opaque);
    }

    // M(ref x), M(out y): what the callee writes through them, thread-variant when its summary says so (a device
    // function) or when any argument is (any other method); an out argument is always written
    private void WriteArguments(IInvocationOperation invocation, ControlFlowGraph graph, ThreadVariance[] state,
        SyntaxNode? opaque)
    {
        var written = TryGetDeviceFunction(invocation.TargetMethod, out var callee, out var declaration)
            ? GetCallVariance(callee, declaration, invocation, graph, state).ByReference
            : EvaluateOperands(invocation, graph, state);
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                var target = argument.Value is IDeclarationExpressionOperation declared
                    ? declared.Expression
                    : argument.Value;
                Assign(target, written, strong: argument.Parameter.RefKind == RefKind.Out, state, opaque);
            }
        }
    }

    // A write to a local or parameter: replaces its variance, or adds to it for a write that may not happen (through a
    // ref local, by ref, or in a lambda or local function to a local declared outside it)
    private void Assign(IOperation target, ThreadVariance variance, bool strong, ThreadVariance[] state,
        SyntaxNode? opaque)
    {
        ISymbol? symbol = target switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null
        };

        // A field or property of a struct local or parameter: the struct keeps its other members, so the write only
        // adds to its variance
        if (symbol == null && GetStructRoot(target) is { } root)
        {
            symbol = root;
            strong = false;
        }

        if (symbol == null || !_symbols.TryGetValue(symbol, out var index))
        {
            return;
        }

        if (strong && (opaque == null || IsDeclaredIn(symbol, opaque)))
        {
            state[index] = variance;
        }
        else
        {
            state[index] |= variance;
        }
    }

    // The local or parameter of struct type whose member target writes (p.X, p.Inner.Y), or null for any other target
    private static ISymbol? GetStructRoot(IOperation target)
    {
        var current = target;
        while (true)
        {
            var instance = current switch
            {
                IFieldReferenceOperation { Instance: { } fieldOwner } => fieldOwner,
                IPropertyReferenceOperation { Instance: { } propertyOwner } => propertyOwner,
                _ => null
            };

            if (instance is not { Type: { IsValueType: true, IsRefLikeType: false } })
            {
                return null;
            }

            switch (instance)
            {
                case ILocalReferenceOperation local:
                    return local.Local;
                case IParameterReferenceOperation parameter:
                    return parameter.Parameter;
                default:
                    current = instance;
                    continue;
            }
        }
    }

    private static bool IsDeclaredIn(ISymbol symbol, SyntaxNode scope) =>
        symbol.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(scope, static (reference, s) =>
                reference.SyntaxTree == s.SyntaxTree && s.Span.Contains(reference.Span)
            );

    // ---- Reading values -----------------------------------------------------------------------------------------

    // The variance of the value of operation; records the variance of each expression while observing
    private ThreadVariance Evaluate(IOperation operation, ControlFlowGraph graph, ThreadVariance[] state)
    {
        var variance = operation switch
        {
            ILocalReferenceOperation local =>
                _symbols.TryGetValue(local.Local, out var index) ? state[index] : ThreadVariance.Uniform,
            IParameterReferenceOperation parameter => ReadParameter(parameter.Parameter, state),
            IFlowCaptureReferenceOperation capture =>
                _captures.TryGetValue((graph, capture.Id), out var captured) ? state[captured] : ThreadVariance.Uniform,
            IPropertyReferenceOperation { Property: var property }
                when SyncIntrinsicClassifier.IsThreadVariantSource(property) => ThreadVariance.Block,
            IPropertyReferenceOperation { Property: var property }
                when SyncIntrinsicClassifier.IsGridVariantSource(property) => ThreadVariance.Grid,
            IInvocationOperation invocation => EvaluateCall(invocation, graph, state),
            IFlowAnonymousFunctionOperation => ThreadVariance.Uniform,
            ISimpleAssignmentOperation assignment => Evaluate(assignment.Value, graph, state),
            ICompoundAssignmentOperation compound =>
                Evaluate(compound.Target, graph, state) | Evaluate(compound.Value, graph, state),
            _ => EvaluateOperands(operation, graph, state)
        };

        if (_observed != null && operation.Syntax is ExpressionSyntax expression)
        {
            _observed[expression] = _observed.TryGetValue(expression, out var seen) ? seen | variance : variance;
        }

        return variance;
    }

    private ThreadVariance EvaluateOperands(IOperation operation, ControlFlowGraph graph, ThreadVariance[] state)
    {
        var variance = ThreadVariance.Uniform;
        foreach (var child in operation.ChildOperations)
        {
            variance |= Evaluate(child, graph, state);
        }

        return variance;
    }

    // A parameter of the analyzed method has a value from the start; one of a lambda or local function that is not
    // defined where it is spliced belongs to a body whose callers are not followed
    private ThreadVariance ReadParameter(IParameterSymbol parameter, ThreadVariance[] state) =>
        _symbols.TryGetValue(parameter, out var index) ? state[index] :
        parameter.ContainingSymbol.IsEqualTo(_method) ? ThreadVariance.Uniform : ThreadVariance.Block;

    // A device function returns what its summary says for the variance of its arguments; an intrinsic that returns a
    // thread-variant value is thread-variant; any other call is as variant as its operands
    private ThreadVariance EvaluateCall(IInvocationOperation invocation, ControlFlowGraph graph, ThreadVariance[] state)
    {
        var method = invocation.TargetMethod;
        if (TryGetDeviceFunction(method, out var callee, out var declaration))
        {
            return GetCallVariance(callee, declaration, invocation, graph, state).Return;
        }

        var operands = EvaluateOperands(invocation, graph, state);
        return !method.ReturnsVoid && SyncIntrinsicClassifier.Classify(method).ResultIsThreadVariant
            ? ThreadVariance.Block
            : operands;
    }

    // A device function with a body in this compilation
    private bool TryGetDeviceFunction(IMethodSymbol method, [NotNullWhen(true)] out IMethodSymbol? callee,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration)
    {
        callee = (method.ReducedFrom ?? method).OriginalDefinition;
        if (SuspensionAnalysis.TryGetDeviceFunctionBody(callee, _model.Compilation, out declaration))
        {
            return true;
        }

        callee = null;
        return false;
    }

    private CalleeVariance GetCallVariance(IMethodSymbol callee, MethodDeclarationSyntax declaration,
        IInvocationOperation invocation, ControlFlowGraph graph, ThreadVariance[] state)
    {
        var masks = GetArgumentMasks(invocation, graph, state);
        return GetCalleeSummary(callee, declaration, masks with { Identity = 0 });
    }

    // Which parameters receive a value that varies within a block, or between blocks only (the identity mask comes from
    // the probe runs: GetIdentityMask)
    private ArgumentMasks GetArgumentMasks(IInvocationOperation invocation, ControlFlowGraph graph,
        ThreadVariance[] state)
    {
        ulong blockMask = 0;
        ulong gridMask = 0;
        foreach (var argument in invocation.Arguments)
        {
            var variance = Evaluate(argument.Value, graph, state);
            if (argument.Parameter is not { } parameter || variance == ThreadVariance.Uniform)
            {
                continue;
            }

            var bit = ParameterBit(parameter.Ordinal);
            if (variance == ThreadVariance.Block)
            {
                blockMask |= bit;
            }
            else
            {
                gridMask |= bit;
            }

        }

        return new ArgumentMasks(blockMask, gridMask, 0);
    }

    private CalleeVariance GetCalleeSummary(IMethodSymbol callee, MethodDeclarationSyntax declaration,
        ArgumentMasks masks)
    {
        var cache = s_callees.GetValue(_model.Compilation,
                static _ => new ConcurrentDictionary<IMethodSymbol,
                    ConcurrentDictionary<ArgumentMasks, CalleeVariance>>(SymbolEqualityComparer.Default))
            .GetOrAdd(callee, static _ => new ConcurrentDictionary<ArgumentMasks, CalleeVariance>());
        if (cache.TryGetValue(masks, out var summary))
        {
            return summary;
        }

        // [WHY] A recursive call adds nothing that the other calls of the cycle do not; it is left out, not cached
        if (!_calleesInProgress.Add(callee))
        {
            return default;
        }

        summary = AnalyzeCallee(callee, declaration, masks);
        _calleesInProgress.Remove(callee);
        return cache.GetOrAdd(masks, summary);
    }

    private CalleeVariance AnalyzeCallee(IMethodSymbol callee, MethodDeclarationSyntax declaration,
        ArgumentMasks masks)
    {
        var model = _model.Compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        if (!MethodFlowGraph.TryCreate(model, declaration, out var graph))
        {
            return default;
        }

        var analysis = new UniformityAnalysis(model, callee, _calleesInProgress);
        analysis.Run(graph, masks);
        return analysis.Summarize();
    }

    // What a call of the analyzed method gives its caller: the variance of what it returns (a value returned under a
    // thread-variant branch included) and of what it leaves in its ref and out parameters
    private CalleeVariance Summarize()
    {
        var returned = ThreadVariance.Uniform;
        var state = new ThreadVariance[_symbols.Count + _captures.Count];
        for (var node = 0; node < _nodes.Count; node++)
        {
            if (_nodes[node] is { ReturnValue: { } value, Graph: { } graph })
            {
                RunNode(node, state);
                returned |= Evaluate(value, graph, state) | _region[node];
            }
        }

        var byReference = ThreadVariance.Uniform;
        foreach (var parameter in _method.Parameters)
        {
            if (parameter.RefKind is RefKind.Ref or RefKind.Out && _symbols.TryGetValue(parameter, out var index))
            {
                byReference |= _in[_exit][index];
            }
        }

        var barrier = SummarizeBarriers();
        return new CalleeVariance(returned, byReference, barrier.Divergence, barrier.Certain);
    }

    /// <summary>What a call of a device function gives its caller.</summary>
    /// <param name="Return">
    /// The variance of the value it returns, a value returned under a divergent branch included.
    /// </param>
    /// <param name="ByReference">The variance of what it leaves in its <c>ref</c> and <c>out</c> parameters.</param>
    /// <param name="Barrier">Whether only some threads reach a barrier in it (§3.2.4, the composed summary).</param>
    /// <param name="Certain">Whether a branch behind <paramref name="Barrier"/> certainly diverges.</param>
    private readonly record struct CalleeVariance(
        ThreadVariance Return,
        ThreadVariance ByReference,
        BarrierDivergence Barrier,
        bool Certain);

    /// <summary>
    /// The parameters that receive a value that varies within a block, one that varies only between blocks, and the
    /// thread index itself.
    /// </summary>
    private readonly record struct ArgumentMasks(ulong Block, ulong Grid, ulong Identity);

    private enum FlowEventKind
    {
        // A parameter receives its variance
        Define,

        // An operation writes locals, parameters or a flow capture
        Write,

        // An operation is evaluated; only read while recording the variance of each expression
        Observe,

        // A call that reaches a barrier; only read while recording the variance of its arguments
        Site
    }

    private readonly struct FlowEvent(
        FlowEventKind kind,
        IOperation? operation,
        ControlFlowGraph? graph,
        SyntaxNode? opaque,
        int value = -1,
        ThreadVariance variance = ThreadVariance.Uniform)
    {
        public FlowEventKind Kind { get; } = kind;

        public IOperation? Operation { get; } = operation;

        public ControlFlowGraph? Graph { get; } = graph;

        // The body of the lambda or local function that the operation is in, when it may run at any time
        public SyntaxNode? Opaque { get; } = opaque;

        public int Value { get; } = value;

        public ThreadVariance Variance { get; } = variance;
    }

    private sealed class FlowNode(int index)
    {
        public int Index { get; } = index;

        public ImmutableArray<FlowEvent>.Builder Events { get; } = ImmutableArray.CreateBuilder<FlowEvent>();

        public ImmutableArray<int>.Builder Successors { get; } = ImmutableArray.CreateBuilder<int>();

        // The condition evaluated at the end of the node when it branches, or the value it returns from the method
        public IOperation? Condition { get; set; }

        public IOperation? ReturnValue { get; set; }

        public ControlFlowGraph? Graph { get; set; }

        // Whether the node calls something that splits the state machine (a barrier, a warp shuffle, a call that
        // suspends), and those calls in order
        public bool HasSplit { get; set; }

        public ImmutableArray<SyntaxNode>.Builder Splits { get; } = ImmutableArray.CreateBuilder<SyntaxNode>();

        // The successors taken when the condition is true and when it is false (-1 when the node does not branch)
        public int WhenTrue { get; set; } = -1;

        public int WhenFalse { get; set; } = -1;

        // The values that the events of the node write
        public ImmutableArray<int>.Builder Written { get; } = ImmutableArray.CreateBuilder<int>();
    }
}
