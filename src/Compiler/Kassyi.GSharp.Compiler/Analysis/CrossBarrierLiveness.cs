using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Finds the locals that are live right after a point where the CPU dialects split the state machine of a kernel or
/// device function, on the control flow graph of its body (GSH0039, GSH0041, GSH0072; A-21, #668).
/// </summary>
/// <remarks>
/// <para>
/// The split points are the calls that split the state machine: the synchronization intrinsics that
/// <see cref="SyncIntrinsicClassifier"/> marks (block and grid barriers, warp shuffles) and the calls of device functions
/// that suspend (<see cref="SuspensionAnalysis"/>). The body of a <c>Gpu.ForUnroll</c> and of a <c>Gpu.SyncGrid(body)</c>
/// is spliced into the graph where it is called, as the lowering expands it: the <c>ForUnroll</c> body as a loop, the
/// <c>SyncGrid</c> body with a split in each gap that <see cref="SyncGridBody.IsSyncPoint"/> places. Other lambdas and
/// local functions are not split; a use of a local inside them counts as a use where the lambda is created or the local
/// function is called.
/// </para>
/// <para>
/// A delegate keeps the locals it captures: the CPU dialects store it in the frame and run it in a later state, where a
/// captured stackalloc pointer would refer to the memory of an earlier call (PR #771). So a captured local is live across
/// every split that the delegate is live across. A delegate stored in a local (a carrier) that is only run or assigned
/// is live as that local is (one more bit of the same fixed point). Any other delegate (passed, copied, stored
/// elsewhere, made inside another lambda or local function) may run after any later split, so its captured locals are
/// pinned where it is made: live across every split reachable from there. The bodies of <c>Gpu</c> macros run within the
/// call and are not delegates.
/// </para>
/// <para>
/// A local is live after a split when some path from the split reads it before writing it. Declaring it, assigning it
/// (rebinding a <c>ref</c> local), passing it as an <c>out</c> argument or naming it as an element of a deconstruction
/// writes it; any other use reads it, also a write through a <c>ref</c> local, a span or a pointer. An assignment whose
/// target is not the local itself (<c>span[i] = v</c>, <c>*p = v</c>, compound, deconstruction, increment) evaluates
/// its value first and its target last, as the lowering splits it (a split in the value comes before the use of the
/// target). The liveness is the classic backward fixed point over the nodes of the graph, one bit per tracked local.
/// </para>
/// <para>
/// The blocks of a <c>finally</c> region have no successor edges in the Roslyn graph. That is harmless today: a barrier
/// in the scope of a <c>using</c> is GSH0003 and <c>try</c> is GSH0038, so no split point lies in such a region.
/// </para>
/// <para>
/// GSH0072 (an iterator local) follows the same liveness: compared with the text order it reports a local used again
/// on the back edge of a loop, and no longer reports one whose later use is on a branch that the barrier does not reach.
/// </para>
/// </remarks>
internal sealed class CrossBarrierLiveness
{
    private readonly SemanticModel _model;

    // The locals that the liveness follows: the tracked locals first, then the carriers (delegate locals)
    private readonly ImmutableArray<ILocalSymbol> _locals;
    private readonly int _trackedCount;

    // For each carrier, the tracked locals that the delegates assigned to it capture
    private readonly ulong[][] _carrierCaptures;
    private readonly ImmutableArray<FlowNode>.Builder _nodes = ImmutableArray.CreateBuilder<FlowNode>();
    private bool _hasPins;

    // The state of the graph being built: the node that receives the next events, the gaps of the Gpu.SyncGrid body
    // being built (null outside one), the element range of the last top-level operation and of the current one
    private FlowNode _current;
    private SyncGridGaps? _gaps;
    private ElementRange? _lastRange;
    private ElementRange? _topRange;

    private CrossBarrierLiveness(SemanticModel model, IReadOnlyList<ILocalSymbol> tracked, SyntaxNode body)
    {
        _model = model;
        _trackedCount = tracked.Count;
        var locals = ImmutableArray.CreateBuilder<ILocalSymbol>(tracked.Count);
        locals.AddRange(tracked);
        AddCarriers(body, locals);
        _locals = locals.ToImmutable();
        _carrierCaptures = new ulong[_locals.Length - _trackedCount][];
        for (var i = 0; i < _carrierCaptures.Length; i++)
        {
            _carrierCaptures[i] = new ulong[WordCount];
        }

        _current = new FlowNode(-1);
    }

    private int WordCount => (_locals.Length + 63) / 64;

    /// <summary>
    /// For each local of <paramref name="tracked"/>, whether it is live right after a split point of
    /// <paramref name="graph"/>, the control flow graph of a method body bound by <paramref name="model"/>.
    /// </summary>
    public static bool[] FindLiveAcrossSplit(ControlFlowGraph graph, IReadOnlyList<ILocalSymbol> tracked,
        SemanticModel model)
    {
        var liveness = new CrossBarrierLiveness(model, tracked, graph.OriginalOperation.Syntax);
        liveness.Build(graph, gaps: null);
        return liveness.Solve();
    }

    // The carriers of body: the delegate locals that are only run (f(), f.Invoke()) or assigned (f = <value>), and not
    // named inside a lambda or local function other than a macro body
    private void AddCarriers(SyntaxNode body, ImmutableArray<ILocalSymbol>.Builder locals)
    {
        var candidates = ImmutableArray.CreateBuilder<ILocalSymbol>();
        var rejected = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax declarator
                    when _model.GetDeclaredSymbol<ILocalSymbol>(declarator) is { IsRef: false, Type.TypeKind: TypeKind.Delegate } local:
                    candidates.Add(local);
                    break;
                case IdentifierNameSyntax name
                    when _model.GetSymbol(name) is ILocalSymbol { Type.TypeKind: TypeKind.Delegate } local &&
                         (!IsRunOrAssigned(name) || IsInsideFunction(name, body)):
                    rejected.Add(local);
                    break;
            }
        }

        foreach (var candidate in candidates)
        {
            if (!rejected.Contains(candidate))
            {
                locals.Add(candidate);
            }
        }
    }

    // f(...), f.Invoke(...), f = <value>
    private static bool IsRunOrAssigned(IdentifierNameSyntax name) => name.Parent switch
    {
        InvocationExpressionSyntax invocation => invocation.Expression == name,
        MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke", Parent: InvocationExpressionSyntax } access =>
            access.Expression == name,
        AssignmentExpressionSyntax assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Left == name,
        _ => false
    };

    // Whether node lies in a lambda or local function of body that is not the body of a Gpu macro
    private bool IsInsideFunction(SyntaxNode node, SyntaxNode body)
    {
        for (var ancestor = node.Parent; ancestor != null && ancestor != body; ancestor = ancestor.Parent)
        {
            if (ancestor is LocalFunctionStatementSyntax ||
                (ancestor is AnonymousFunctionExpressionSyntax && !GpuMacroClassifier.ClassifyBody(ancestor, _model).HasBody))
            {
                return true;
            }
        }

        return false;
    }

    // Whether running body can make a delegate that outlives the run: a lambda that is not a macro body, or a local
    // function used as a value
    private bool MakesDelegate(SyntaxNode body)
    {
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case AnonymousFunctionExpressionSyntax lambda when !GpuMacroClassifier.ClassifyBody(lambda, _model).HasBody:
                    return true;
                case IdentifierNameSyntax name
                    when _model.GetSymbol(name) is IMethodSymbol { MethodKind: MethodKind.LocalFunction } &&
                         !(name.Parent is InvocationExpressionSyntax invocation && invocation.Expression == name):
                    return true;
            }
        }

        return false;
    }

    // Builds the nodes of graph; returns the node of its entry block and the last node of its exit block
    private (FlowNode Entry, FlowNode Exit) Build(ControlFlowGraph graph, SyncGridGaps? gaps)
    {
        var (savedCurrent, savedGaps, savedLast, savedTop) = (_current, _gaps, _lastRange, _topRange);
        _gaps = gaps;

        var blocks = graph.Blocks;
        var starts = new FlowNode[blocks.Length];
        var ends = new FlowNode[blocks.Length];
        for (var i = 0; i < blocks.Length; i++)
        {
            _current = NewNode();
            _lastRange = null;
            starts[i] = _current;
            foreach (var operation in blocks[i].Operations)
            {
                WalkTop(operation, graph);
            }

            if (blocks[i].BranchValue is { } branchValue)
            {
                WalkTop(branchValue, graph);
            }

            ends[i] = _current;
        }

        for (var i = 0; i < blocks.Length; i++)
        {
            ConnectBlock(blocks, starts, ends, i, blocks[i].FallThroughSuccessor?.Destination);
            ConnectBlock(blocks, starts, ends, i, blocks[i].ConditionalSuccessor?.Destination);
        }

        var result = (starts[0], ends[blocks.Length - 1]);
        (_current, _gaps, _lastRange, _topRange) = (savedCurrent, savedGaps, savedLast, savedTop);
        return result;
    }

    // An edge between two blocks; in a Gpu.SyncGrid body, through a split node when the edge crosses a gap
    private void ConnectBlock(ImmutableArray<BasicBlock> blocks, FlowNode[] starts, FlowNode[] ends, int source,
        BasicBlock? destination)
    {
        if (destination == null)
        {
            return;
        }

        var target = destination.Ordinal;
        if (_gaps is { } gaps &&
            RangeBefore(blocks, ends, source, blocks.Length) is { } before &&
            RangeAfter(blocks, starts, target, blocks.Length) is { } after &&
            gaps.HasGap(before.Last, after.First))
        {
            var split = NewNode();
            split.Events.Add(FlowEvent.Split);
            Connect(ends[source], split);
            Connect(split, starts[target]);
            return;
        }

        Connect(ends[source], starts[target]);
    }

    // The element range of the last operation run before leaving block, looking back through empty blocks
    private static ElementRange? RangeBefore(ImmutableArray<BasicBlock> blocks, FlowNode[] ends, int block, int depth)
    {
        if (ends[block].Last is { } last)
        {
            return last;
        }

        var predecessors = blocks[block].Predecessors;
        return depth > 0 && predecessors.Length == 1
            ? RangeBefore(blocks, ends, predecessors[0].Source.Ordinal, depth - 1)
            : null;
    }

    // The element range of the first operation run after entering block, looking ahead through empty blocks
    private static ElementRange? RangeAfter(ImmutableArray<BasicBlock> blocks, FlowNode[] starts, int block, int depth)
    {
        if (starts[block].First is { } first)
        {
            return first;
        }

        var basicBlock = blocks[block];
        return depth > 0 && basicBlock.ConditionalSuccessor == null &&
               basicBlock.FallThroughSuccessor?.Destination is { } next
            ? RangeAfter(blocks, starts, next.Ordinal, depth - 1)
            : null;
    }

    // An operation of a block: in a Gpu.SyncGrid body, a split comes first when a gap lies between it and the
    // operation before it in the same block
    private void WalkTop(IOperation operation, ControlFlowGraph graph)
    {
        _topRange = _gaps?.RangeOf(operation.Syntax);
        if (_topRange is { } range && _gaps is { } gaps)
        {
            if (_lastRange is { } last && gaps.HasGap(last.Last, range.First))
            {
                _current.Events.Add(FlowEvent.Split);
            }

            _current.First ??= range;
        }

        Walk(operation, graph);

        if (_topRange is { } walked)
        {
            _current.First ??= walked;
            _current.Last = walked;
            _lastRange = walked;
        }
    }

    // The events of an operation in the order C# evaluates it
    private void Walk(IOperation operation, ControlFlowGraph graph)
    {
        switch (operation)
        {
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment
                when IndexOf(target.Local) is var index and >= 0:
                if (index >= _trackedCount && GetDelegateBody(assignment.Value) is { } body)
                {
                    AddDelegate(body, index);
                }
                else
                {
                    Walk(assignment.Value, graph);
                }

                // Assigning through a ref local uses the reference; declaring or rebinding it replaces it
                _current.Events.Add(target.IsDeclaration || assignment.IsRef || !target.Local.IsRef
                    ? FlowEvent.Write(index)
                    : FlowEvent.Read(index));
                return;
            case ILocalReferenceOperation reference when IndexOf(reference.Local) is var index and >= 0:
                _current.Events.Add(FlowEvent.Read(index));
                return;
            case IDeconstructionAssignmentOperation deconstruction:
                Walk(deconstruction.Value, graph);
                WalkDeconstructionTarget(deconstruction.Target, graph);
                return;
            case IAssignmentOperation assignment:
                // span[i] = <value>, *p = <value>, x += <value>: the value first, then the target
                Walk(assignment.Value, graph);
                Walk(assignment.Target, graph);
                return;
            case IInvocationOperation invocation:
                WalkInvocation(invocation, graph);
                return;
            case IFlowAnonymousFunctionOperation function:
                AddDelegate(function.Syntax, carrier: -1);
                return;
            case IDelegateCreationOperation { Target: IMethodReferenceOperation reference }
                when GetDelegateBody(reference) is { } localFunction:
                AddDelegate(localFunction, carrier: -1);
                return;
        }

        foreach (var child in operation.ChildOperations)
        {
            Walk(child, graph);
        }
    }

    // (a, span) = <value>: an element that is a tracked local itself is written, any other element is walked as a target
    private void WalkDeconstructionTarget(IOperation target, ControlFlowGraph graph)
    {
        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                WalkDeconstructionTarget(element, graph);
            }

            return;
        }

        if (target is ILocalReferenceOperation local && IndexOf(local.Local) is var index and >= 0 && !local.Local.IsRef)
        {
            _current.Events.Add(FlowEvent.Write(index));
            return;
        }

        Walk(target, graph);
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

        foreach (var argument in invocation.Arguments)
        {
            // The body of Gpu.SingleThread or Gpu.GridStrideLoop runs within the call
            if (macro.HasBody && argument.Parameter?.Ordinal == macro.LambdaArgumentIndex &&
                UnwrapLambda(argument.Value) is { } macroBody)
            {
                AddRun(macroBody.Syntax);
            }
            else if (!IsOutLocal(argument, out _))
            {
                Walk(argument.Value, graph);
            }
        }

        if (method.MethodKind == MethodKind.LocalFunction && method.TryGetDeclaringSyntax(out LocalFunctionStatementSyntax? localFunction))
        {
            AddRun(localFunction);
        }

        if (SyncIntrinsicClassifier.Classify(method).SplitsStateMachine ||
            SuspensionAnalysis.IsSuspendingFunction(method, _model.Compilation))
        {
            _current.Events.Add(FlowEvent.Split);
        }

        // The callee writes an out argument when it returns, after any split
        foreach (var argument in invocation.Arguments)
        {
            if (IsOutLocal(argument, out var index))
            {
                _current.Events.Add(FlowEvent.Write(index));
            }
        }
    }

    // M(out local): a tracked local that the call writes
    private bool IsOutLocal(IArgumentOperation argument, out int index)
    {
        index = argument is { Parameter.RefKind: RefKind.Out, Value: ILocalReferenceOperation { Local: { IsRef: false } local } }
            ? IndexOf(local)
            : -1;
        return index >= 0;
    }

    // <arguments> <body>: the body of the lambda where it is called, once for SyncGrid with a split in each gap, in a
    // loop for ForUnroll; the call itself does not split
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

        if (body == null)
        {
            return;
        }

        var gaps = macro.Kind == GpuMacroKind.SyncGrid && body.Syntax is LambdaExpressionSyntax syntax
            ? new SyncGridGaps(SyncGridBody.Flatten(syntax, _model).Elements, _model)
            : null;
        var before = _current;
        var range = _topRange;
        var (entry, exit) = Build(graph.GetAnonymousFunctionControlFlowGraphInScope(body), gaps);
        var after = NewNode();
        after.First = range;
        after.Last = range;
        Connect(before, entry);
        Connect(exit, after);
        if (macro.Kind == GpuMacroKind.ForUnroll)
        {
            Connect(exit, entry);
        }

        _current = after;
    }

    private static IFlowAnonymousFunctionOperation? UnwrapLambda(IOperation operation) => operation switch
    {
        IFlowAnonymousFunctionOperation lambda => lambda,
        IDelegateCreationOperation creation => UnwrapLambda(creation.Target),
        IConversionOperation conversion => UnwrapLambda(conversion.Operand),
        _ => null
    };

    // The lambda or local function that makes the delegate value is: its syntax, or null for any other value
    private static SyntaxNode? GetDelegateBody(IOperation value) => value switch
    {
        IFlowAnonymousFunctionOperation lambda => lambda.Syntax,
        IDelegateCreationOperation creation => GetDelegateBody(creation.Target),
        IConversionOperation conversion => GetDelegateBody(conversion.Operand),
        IMethodReferenceOperation { Method: { MethodKind: MethodKind.LocalFunction } method }
            when method.TryGetDeclaringSyntax(out LocalFunctionStatementSyntax? localFunction) => localFunction,
        _ => null
    };

    // A lambda or local function that runs here (a macro body, a local function call): it uses its captured locals here,
    // and a delegate it makes keeps them from here on
    private void AddRun(SyntaxNode body) => AddCaptures(body, carrier: null, pin: MakesDelegate(body));

    // A delegate made here from body: kept by carrier (an index of _locals), or by anything when carrier is -1
    private void AddDelegate(SyntaxNode body, int carrier)
    {
        var isCarried = carrier >= 0 && !MakesDelegate(body);
        AddCaptures(body, isCarried ? _carrierCaptures[carrier - _trackedCount] : null, pin: !isCarried);
    }

    // A use of a tracked local of this graph inside a lambda or local function that is not split counts as a use here;
    // a local declared inside it belongs to it and is not a use here. Each captured local is also added to carrier, or
    // pinned here.
    private void AddCaptures(SyntaxNode body, ulong[]? carrier, bool pin)
    {
        foreach (var node in body.DescendantNodes())
        {
            if (node is not IdentifierNameSyntax identifier ||
                _model.GetSymbol(identifier) is not ILocalSymbol local ||
                IsDeclaredIn(local, body) ||
                IndexOf(local) is not (var index and >= 0))
            {
                continue;
            }

            _current.Events.Add(FlowEvent.Read(index));
            if (index >= _trackedCount)
            {
                continue;
            }

            if (carrier != null)
            {
                carrier[index / 64] |= 1UL << (index % 64);
            }

            if (pin)
            {
                _current.Events.Add(FlowEvent.Pin(index));
                _hasPins = true;
            }
        }
    }

    private static bool IsDeclaredIn(ILocalSymbol local, SyntaxNode syntax) =>
        local.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(syntax, static (reference, scope) =>
                reference.SyntaxTree == scope.SyntaxTree && scope.Span.Contains(reference.Span)
            );

    private int IndexOf(ILocalSymbol local) =>
        _locals
            .AsStackEnumerable()
            .IndexOf(local, static (tracked, target) => tracked.IsEqualTo(target));

    private FlowNode NewNode()
    {
        var node = new FlowNode(_nodes.Count);
        _nodes.Add(node);
        return node;
    }

    private static void Connect(FlowNode from, FlowNode to) => from.Successors.Add(to.Index);

    // The backward fixed point of the live locals, then the locals live after each split: also those that a carrier live
    // after it captures, and those pinned before it
    private bool[] Solve()
    {
        var words = WordCount;
        var liveIn = new ulong[_nodes.Count][];
        for (var i = 0; i < liveIn.Length; i++)
        {
            liveIn[i] = new ulong[words];
        }

        var live = new ulong[words];
        bool changed;
        do
        {
            changed = false;
            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                LiveOut(_nodes[i], liveIn, live);
                ApplyBackward(_nodes[i], live, crossing: null);
                if (!live.AsSpan().SequenceEqual(liveIn[i]))
                {
                    live.AsSpan().CopyTo(liveIn[i]);
                    changed = true;
                }
            }
        } while (changed);

        var crossing = new ulong[words];
        foreach (var node in _nodes)
        {
            LiveOut(node, liveIn, live);
            ApplyBackward(node, live, crossing);
        }

        for (var carrier = 0; carrier < _carrierCaptures.Length; carrier++)
        {
            if (IsSet(crossing, _trackedCount + carrier))
            {
                Or(crossing, _carrierCaptures[carrier]);
            }
        }

        if (_hasPins)
        {
            AddPinnedAcrossSplit(crossing);
        }

        var result = new bool[_trackedCount];
        for (var local = 0; local < result.Length; local++)
        {
            result[local] = IsSet(crossing, local);
        }

        return result;
    }

    // The forward fixed point of the pinned locals: a local pinned on some path to a split is added to crossing
    private void AddPinnedAcrossSplit(ulong[] crossing)
    {
        var pinnedIn = new ulong[_nodes.Count][];
        for (var i = 0; i < pinnedIn.Length; i++)
        {
            pinnedIn[i] = new ulong[crossing.Length];
        }

        var pinned = new ulong[crossing.Length];
        bool changed;
        do
        {
            changed = false;
            foreach (var node in _nodes)
            {
                pinnedIn[node.Index].AsSpan().CopyTo(pinned);
                foreach (var flowEvent in node.Events)
                {
                    switch (flowEvent.Kind)
                    {
                        case FlowEventKind.Pin:
                            pinned[flowEvent.Local / 64] |= 1UL << (flowEvent.Local % 64);
                            break;
                        case FlowEventKind.Split:
                            Or(crossing, pinned);
                            break;
                    }
                }

                foreach (var successor in node.Successors)
                {
                    changed |= Or(pinnedIn[successor], pinned);
                }
            }
        } while (changed);
    }

    // target |= bits; whether target changed
    private static bool Or(ulong[] target, ulong[] bits)
    {
        var changed = false;
        for (var word = 0; word < target.Length; word++)
        {
            var merged = target[word] | bits[word];
            changed |= merged != target[word];
            target[word] = merged;
        }

        return changed;
    }

    // The union of the live-in sets of the successors of node, into live
    private static void LiveOut(FlowNode node, ulong[][] liveIn, ulong[] live)
    {
        Array.Clear(live, 0, live.Length);
        foreach (var successor in node.Successors)
        {
            var successorLive = liveIn[successor];
            for (var word = 0; word < live.Length; word++)
            {
                live[word] |= successorLive[word];
            }
        }
    }

    // Runs the events of node backward from the locals live at its end; adds to crossing the locals live after a split
    private static void ApplyBackward(FlowNode node, ulong[] live, ulong[]? crossing)
    {
        for (var i = node.Events.Count - 1; i >= 0; i--)
        {
            var flowEvent = node.Events[i];
            switch (flowEvent.Kind)
            {
                case FlowEventKind.Read:
                    live[flowEvent.Local / 64] |= 1UL << (flowEvent.Local % 64);
                    break;
                case FlowEventKind.Write:
                    live[flowEvent.Local / 64] &= ~(1UL << (flowEvent.Local % 64));
                    break;
                case FlowEventKind.Split when crossing != null:
                    for (var word = 0; word < live.Length; word++)
                    {
                        crossing[word] |= live[word];
                    }

                    break;
            }
        }
    }

    private static bool IsSet(ulong[] bits, int index) => (bits[index / 64] & (1UL << (index % 64))) != 0;

    private enum FlowEventKind
    {
        Read,
        Write,
        Split,

        // A delegate that may run after any later split captures the local (forward, AddPinnedAcrossSplit)
        Pin
    }

    private readonly struct FlowEvent(FlowEventKind kind, int local)
    {
        public static readonly FlowEvent Split = new(FlowEventKind.Split, -1);

        public FlowEventKind Kind { get; } = kind;

        public int Local { get; } = local;

        public static FlowEvent Read(int local) => new(FlowEventKind.Read, local);

        public static FlowEvent Write(int local) => new(FlowEventKind.Write, local);

        public static FlowEvent Pin(int local) => new(FlowEventKind.Pin, local);
    }

    // The statements of a Gpu.SyncGrid body that an operation belongs to, by index in SyncGridBody.Flatten
    private readonly record struct ElementRange(int First, int Last);

    private sealed class FlowNode(int index)
    {
        public int Index { get; } = index;

        public ImmutableArray<FlowEvent>.Builder Events { get; } = ImmutableArray.CreateBuilder<FlowEvent>();

        public ImmutableArray<int>.Builder Successors { get; } = ImmutableArray.CreateBuilder<int>();

        // The element ranges of the first and the last top-level operation of the node in a Gpu.SyncGrid body
        public ElementRange? First { get; set; }

        public ElementRange? Last { get; set; }
    }

    // The gaps of a Gpu.SyncGrid body, between its flattened statements (SyncGridBody)
    private sealed class SyncGridGaps(ImmutableArray<SyncGridElement> elements, SemanticModel model)
    {
        // The statement that contains syntax, or the statements that syntax contains (a nested Gpu.SyncGrid(body))
        public ElementRange? RangeOf(SyntaxNode syntax)
        {
            var first = -1;
            var last = -1;
            for (var i = 0; i < elements.Length; i++)
            {
                var span = elements[i].Node.Span;
                if (span.Contains(syntax.Span))
                {
                    return new ElementRange(i, i);
                }

                if (syntax.Span.Contains(span))
                {
                    first = first < 0 ? i : first;
                    last = i;
                }
            }

            return first < 0 ? null : new ElementRange(first, last);
        }

        // Whether a grid sync follows one of the statements from 'from' up to the one before 'to'
        public bool HasGap(int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                if (SyncGridBody.IsSyncPoint(elements, i, model))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
