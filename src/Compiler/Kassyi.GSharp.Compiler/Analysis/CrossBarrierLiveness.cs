using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;
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
/// pinned where it is made: live across every split reachable from there. The bodies of <c>Gpu</c> macros and the lambdas
/// passed to StackLinq operators run within the call (the transpiler expands them in place) and are not delegates.
/// A body uses, and may make delegates of, what the local functions it calls use, directly or through each other.
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
/// <para>
/// A tracked by-value parameter is passed again when the state machine resumes, so it holds its argument after a split
/// unless it was changed before. It counts only after a split that a change reaches on some path (a forward fixed point)
/// and where it is live. Assigning it, passing it as an <c>out</c> or <c>ref</c> argument, naming it as an element of a
/// deconstruction, taking a reference to it or its address, writing one of its fields or calling a method that may
/// change it (not <c>readonly</c>) changes it (GSH0039, #965).
/// </para>
/// </remarks>
internal sealed class CrossBarrierLiveness
{
    private readonly SemanticModel _model;

    // The variables that the liveness follows: the tracked locals and parameters first, then the carriers (delegate
    // locals)
    private readonly ImmutableArray<ISymbol> _locals;
    private readonly int _trackedCount;

    // For each carrier, the tracked locals that the delegates assigned to it capture
    private readonly ulong[][] _carrierCaptures;
    private readonly ImmutableArray<LivenessNode>.Builder _nodes = ImmutableArray.CreateBuilder<LivenessNode>();
    private bool _hasPins;
    private bool _hasChanges;

    // The flow captures of a tracked parameter or of one of its fields (a target that the graph spills before a
    // conditional value), by capture: the parameter, and whether the capture is the whole parameter
    private readonly Dictionary<CaptureId, (int Parameter, bool IsWhole)> _parameterCaptures = new();

    // The state of the graph being built: the node that receives the next events, the gaps of the Gpu.SyncGrid body
    // being built (null outside one), the element range of the last top-level operation and of the current one
    private LivenessNode _current;
    private SyncGridGaps? _gaps;
    private SyncGridElementRange? _lastRange;
    private SyncGridElementRange? _topRange;

    private CrossBarrierLiveness(SemanticModel model, IReadOnlyList<ISymbol> tracked, SyntaxNode body)
    {
        _model = model;
        _trackedCount = tracked.Count;
        var locals = ImmutableArray.CreateBuilder<ISymbol>(tracked.Count);
        locals.AddRange(tracked);
        AddCarriers(body, locals);
        _locals = locals.ToImmutable();
        _carrierCaptures = new ulong[_locals.Length - _trackedCount][];
        for (var i = 0; i < _carrierCaptures.Length; i++)
        {
            _carrierCaptures[i] = new ulong[WordCount];
        }

        _current = new LivenessNode(-1);
    }

    private int WordCount => (_locals.Length + 63) / 64;

    /// <summary>
    /// For each local of <paramref name="tracked"/>, whether it is live right after a split point of
    /// <paramref name="graph"/>, the control flow graph of a method body bound by <paramref name="model"/>; for each
    /// by-value parameter of the method, whether it is live right after a split point that a change of it reaches.
    /// </summary>
    public static bool[] FindLiveAcrossSplit(ControlFlowGraph graph, IReadOnlyList<ISymbol> tracked,
        SemanticModel model)
    {
        var liveness = new CrossBarrierLiveness(model, tracked, graph.OriginalOperation.Syntax);
        liveness.Build(graph, gaps: null);
        return liveness.Solve();
    }

    // The carriers of body: the delegate locals that are only run (f(), f.Invoke()) or assigned (f = <value>), and not
    // named inside a lambda or local function other than a macro body
    private void AddCarriers(SyntaxNode body, ImmutableArray<ISymbol>.Builder locals)
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

    // Whether node lies in a lambda of body that does not run in place, or in a local function of body
    private bool IsInsideFunction(SyntaxNode node, SyntaxNode body)
    {
        for (var ancestor = node.Parent; ancestor != null && ancestor != body; ancestor = ancestor.Parent)
        {
            if (ancestor is LocalFunctionStatementSyntax ||
                (ancestor is AnonymousFunctionExpressionSyntax && !RunsInPlace(ancestor)))
            {
                return true;
            }
        }

        return false;
    }

    // Whether running bodies can make a delegate that outlives the run: a lambda that does not run in place, or a local
    // function used as a value
    private bool MakesDelegate(ImmutableArray<SyntaxNode>.Builder bodies)
    {
        foreach (var body in bodies)
        {
            foreach (var node in body.DescendantNodes())
            {
                switch (node)
                {
                    case AnonymousFunctionExpressionSyntax lambda when !RunsInPlace(lambda):
                        return true;
                    case IdentifierNameSyntax name
                        when _model.GetSymbol(name) is IMethodSymbol { MethodKind: MethodKind.LocalFunction } &&
                             !(name.Parent is InvocationExpressionSyntax invocation && invocation.Expression == name):
                        return true;
                }
            }
        }

        return false;
    }

    // A lambda that runs within the call it is passed to and is never stored: the body of a Gpu macro, or a lambda passed
    // to a StackLinq operator, which the transpiler expands into code that runs it in place
    private bool RunsInPlace(SyntaxNode lambda) =>
        GpuMacroClassifier.ClassifyBody(lambda, _model).HasBody ||
        (lambda.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } } &&
         _model.GetSymbol(call) is IMethodSymbol method &&
         StackLinqHelper.IsStackLinqMethod(method));

    // body, and the local functions that it calls, directly or through each other: running body runs them too
    private ImmutableArray<SyntaxNode>.Builder GetRunBodies(SyntaxNode body)
    {
        var bodies = ImmutableArray.CreateBuilder<SyntaxNode>();
        bodies.Add(body);
        for (var i = 0; i < bodies.Count; i++)
        {
            foreach (var node in bodies[i].DescendantNodes())
            {
                if (node is InvocationExpressionSyntax invocation &&
                    _model.GetSymbol(invocation) is IMethodSymbol { MethodKind: MethodKind.LocalFunction } callee &&
                    callee.TryGetDeclaringSyntax(out LocalFunctionStatementSyntax? localFunction) &&
                    !bodies.Contains(localFunction))
                {
                    bodies.Add(localFunction);
                }
            }
        }

        return bodies;
    }

    // Builds the nodes of graph; returns the node of its entry block and the last node of its exit block
    private (LivenessNode Entry, LivenessNode Exit) Build(ControlFlowGraph graph, SyncGridGaps? gaps)
    {
        var (savedCurrent, savedGaps, savedLast, savedTop) = (_current, _gaps, _lastRange, _topRange);
        _gaps = gaps;

        var blocks = graph.Blocks;
        var starts = new LivenessNode[blocks.Length];
        var ends = new LivenessNode[blocks.Length];
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
    private void ConnectBlock(ImmutableArray<BasicBlock> blocks, LivenessNode[] starts, LivenessNode[] ends, int source,
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
            split.Events.Add(LivenessEvent.Split);
            Connect(ends[source], split);
            Connect(split, starts[target]);
            return;
        }

        Connect(ends[source], starts[target]);
    }

    // The element range of the last operation run before leaving block, looking back through empty blocks
    private static SyncGridElementRange? RangeBefore(ImmutableArray<BasicBlock> blocks, LivenessNode[] ends, int block, int depth)
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
    private static SyncGridElementRange? RangeAfter(ImmutableArray<BasicBlock> blocks, LivenessNode[] starts, int block, int depth)
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
                _current.Events.Add(LivenessEvent.Split);
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
                    ? LivenessEvent.Write(index)
                    : LivenessEvent.Read(index));
                return;
            case ISimpleAssignmentOperation { Target: IParameterReferenceOperation or IFlowCaptureReferenceOperation } assignment
                when IndexOfWholeParameter(assignment.Target) is var index and >= 0:
                Walk(assignment.Value, graph);
                _current.Events.Add(LivenessEvent.Write(index));
                AddChange(index);
                return;
            case IFlowCaptureOperation capture when IndexOfRootParameter(capture.Value, out var isWhole) is var index and >= 0:
                _parameterCaptures[capture.Id] = (index, isWhole);
                _current.Events.Add(LivenessEvent.Read(index));
                return;
            case IFlowCaptureReferenceOperation reference when _parameterCaptures.TryGetValue(reference.Id, out var captured):
                if (MayChange(reference))
                {
                    AddChange(captured.Parameter);
                }

                return;
            case ILocalReferenceOperation reference when IndexOf(reference.Local) is var index and >= 0:
                _current.Events.Add(LivenessEvent.Read(index));
                return;
            case IParameterReferenceOperation reference when IndexOf(reference.Parameter) is var index and >= 0:
                _current.Events.Add(LivenessEvent.Read(index));
                if (MayChange(reference))
                {
                    AddChange(index);
                }

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
            _current.Events.Add(LivenessEvent.Write(index));
            return;
        }

        if (target is IParameterReferenceOperation parameter && IndexOf(parameter.Parameter) is var parameterIndex and >= 0)
        {
            _current.Events.Add(LivenessEvent.Write(parameterIndex));
            AddChange(parameterIndex);
            return;
        }

        Walk(target, graph);
    }

    private void AddChange(int index)
    {
        _current.Events.Add(LivenessEvent.Change(index));
        _hasChanges = true;
    }

    // The tracked parameter that reference names as a whole, directly or through a flow capture; -1 for any other
    // operation
    private int IndexOfWholeParameter(IOperation reference) => reference switch
    {
        IParameterReferenceOperation parameter => IndexOf(parameter.Parameter),
        IFlowCaptureReferenceOperation capture
            when _parameterCaptures.TryGetValue(capture.Id, out var captured) && captured.IsWhole => captured.Parameter,
        _ => -1
    };

    // The tracked parameter that value is or is a field of (p, p.X, p.X.Y); -1 for any other operation
    private int IndexOfRootParameter(IOperation value, out bool isWhole)
    {
        isWhole = true;
        while (value is IFieldReferenceOperation { Instance: { } instance })
        {
            value = instance;
            isWhole = false;
        }

        return value is IParameterReferenceOperation parameter ? IndexOf(parameter.Parameter) : -1;
    }

    // Whether the use of a tracked parameter (or of its flow capture) may change it: as the target of an assignment or an
    // increment (also of one of its fields), as a ref or out argument, as the receiver of a method that is not readonly,
    // as an element of a deconstruction, or where a reference to it or its address is taken. A plain assignment of the
    // parameter itself is written by the walk.
    private static bool MayChange(IOperation reference)
    {
        var use = reference;
        while (use.Parent is IFieldReferenceOperation field && field.Instance == use)
        {
            use = field;
        }

        return use.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == use || assignment is ISimpleAssignmentOperation { IsRef: true },
            IIncrementOrDecrementOperation increment => increment.Target == use,
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
            IInvocationOperation invocation => invocation.Instance == use && MayChangeReceiver(invocation.TargetMethod),
            IAddressOfOperation => true,
            ITupleOperation tuple => IsDeconstructionTarget(tuple),
            _ => false
        };
    }

    // A method that may change the struct it is called on
    private static bool MayChangeReceiver(IMethodSymbol method) =>
        !method.IsStatic && !method.IsReadOnly && method.ContainingType is { IsValueType: true, IsReadOnly: false };

    // An element of the left side of a deconstruction, ((a, b), c) = <value>
    private static bool IsDeconstructionTarget(ITupleOperation tuple)
    {
        IOperation current = tuple;
        while (current.Parent is ITupleOperation outer)
        {
            current = outer;
        }

        return current.Parent is IDeconstructionAssignmentOperation deconstruction && deconstruction.Target == current;
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

        var runsLambdasInPlace = StackLinqHelper.IsStackLinqMethod(method);
        foreach (var argument in invocation.Arguments)
        {
            // The body of Gpu.SingleThread or Gpu.GridStrideLoop, and a lambda passed to a StackLinq operator, run within
            // the call
            if ((runsLambdasInPlace || (macro.HasBody && argument.Parameter?.Ordinal == macro.LambdaArgumentIndex)) &&
                UnwrapLambda(argument.Value) is { } inPlace)
            {
                AddRun(inPlace.Syntax);
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
            _current.Events.Add(LivenessEvent.Split);
        }

        // The callee writes an out argument when it returns, after any split
        foreach (var argument in invocation.Arguments)
        {
            if (IsOutLocal(argument, out var index))
            {
                _current.Events.Add(LivenessEvent.Write(index));
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
    private void AddRun(SyntaxNode body)
    {
        var bodies = GetRunBodies(body);
        AddCaptures(bodies, carrier: null, pin: MakesDelegate(bodies));
    }

    // A delegate made here from body: kept by carrier (an index of _locals), or by anything when carrier is -1
    private void AddDelegate(SyntaxNode body, int carrier)
    {
        var bodies = GetRunBodies(body);
        var isCarried = carrier >= 0 && !MakesDelegate(bodies);
        AddCaptures(bodies, isCarried ? _carrierCaptures[carrier - _trackedCount] : null, pin: !isCarried);
    }

    // A use of a tracked local of this graph inside the lambdas and local functions that are not split counts as a use
    // here; a local declared inside one of them belongs to it and is not a use here. Each captured local is also added
    // to carrier, or pinned here.
    private void AddCaptures(ImmutableArray<SyntaxNode>.Builder bodies, ulong[]? carrier, bool pin)
    {
        foreach (var body in bodies)
        {
            AddCaptures(body, bodies, carrier, pin);
        }
    }

    private void AddCaptures(SyntaxNode body, ImmutableArray<SyntaxNode>.Builder bodies, ulong[]? carrier, bool pin)
    {
        foreach (var node in body.DescendantNodes())
        {
            if (node is not IdentifierNameSyntax identifier ||
                _model.GetSymbol(identifier) is not ILocalSymbol local ||
                IsDeclaredInAny(local, bodies) ||
                IndexOf(local) is not (var index and >= 0))
            {
                continue;
            }

            _current.Events.Add(LivenessEvent.Read(index));
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
                _current.Events.Add(LivenessEvent.Pin(index));
                _hasPins = true;
            }
        }
    }

    private static bool IsDeclaredInAny(ILocalSymbol local, ImmutableArray<SyntaxNode>.Builder bodies)
    {
        foreach (var body in bodies)
        {
            if (IsDeclaredIn(local, body))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeclaredIn(ILocalSymbol local, SyntaxNode syntax) =>
        local.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(syntax, static (reference, scope) =>
                reference.SyntaxTree == scope.SyntaxTree && scope.Span.Contains(reference.Span)
            );

    private int IndexOf(ISymbol local) =>
        _locals
            .AsStackEnumerable()
            .IndexOf(local, static (tracked, target) => tracked.IsEqualTo(target));

    private LivenessNode NewNode()
    {
        var node = new LivenessNode(_nodes.Count);
        _nodes.Add(node);
        return node;
    }

    private static void Connect(LivenessNode from, LivenessNode to) => from.Successors.Add(to.Index);

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

        var changedCrossing = _hasChanges ? FindChangedLiveAcrossSplit(liveIn) : new ulong[words];
        var result = new bool[_trackedCount];
        for (var local = 0; local < result.Length; local++)
        {
            result[local] = _locals[local] is IParameterSymbol
                ? IsSet(changedCrossing, local)
                : IsSet(crossing, local);
        }

        return result;
    }

    // The variables live right after a split that a change of them reaches: the live set after each split (backward
    // from the solved live-in sets), then the forward fixed point of the changed variables
    private ulong[] FindChangedLiveAcrossSplit(ulong[][] liveIn)
    {
        var words = WordCount;
        var liveAfterSplit = new ulong[_nodes.Count][][];
        var live = new ulong[words];
        foreach (var node in _nodes)
        {
            var atEvent = liveAfterSplit[node.Index] = new ulong[node.Events.Count][];
            LiveOut(node, liveIn, live);
            for (var i = node.Events.Count - 1; i >= 0; i--)
            {
                if (node.Events[i].Kind == LivenessEventKind.Split)
                {
                    atEvent[i] = (ulong[])live.Clone();
                }

                ApplyBackward(node.Events[i], live);
            }
        }

        var crossing = new ulong[words];
        var changedIn = new ulong[_nodes.Count][];
        for (var i = 0; i < changedIn.Length; i++)
        {
            changedIn[i] = new ulong[words];
        }

        var changed = new ulong[words];
        bool grew;
        do
        {
            grew = false;
            foreach (var node in _nodes)
            {
                changedIn[node.Index].AsSpan().CopyTo(changed);
                for (var i = 0; i < node.Events.Count; i++)
                {
                    var flowEvent = node.Events[i];
                    if (flowEvent.Kind == LivenessEventKind.Change)
                    {
                        changed[flowEvent.Local / 64] |= 1UL << (flowEvent.Local % 64);
                    }
                    else if (flowEvent.Kind == LivenessEventKind.Split)
                    {
                        var liveAfter = liveAfterSplit[node.Index][i];
                        for (var word = 0; word < words; word++)
                        {
                            crossing[word] |= changed[word] & liveAfter[word];
                        }
                    }
                }

                foreach (var successor in node.Successors)
                {
                    grew |= Or(changedIn[successor], changed);
                }
            }
        } while (grew);

        return crossing;
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
                        case LivenessEventKind.Pin:
                            pinned[flowEvent.Local / 64] |= 1UL << (flowEvent.Local % 64);
                            break;
                        case LivenessEventKind.Split:
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
    private static void LiveOut(LivenessNode node, ulong[][] liveIn, ulong[] live)
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
    private static void ApplyBackward(LivenessNode node, ulong[] live, ulong[]? crossing)
    {
        for (var i = node.Events.Count - 1; i >= 0; i--)
        {
            var flowEvent = node.Events[i];
            if (flowEvent.Kind == LivenessEventKind.Split && crossing != null)
            {
                for (var word = 0; word < live.Length; word++)
                {
                    crossing[word] |= live[word];
                }
            }

            ApplyBackward(flowEvent, live);
        }
    }

    // A read makes the variable live before the event, a write ends its liveness
    private static void ApplyBackward(LivenessEvent flowEvent, ulong[] live)
    {
        switch (flowEvent.Kind)
        {
            case LivenessEventKind.Read:
                live[flowEvent.Local / 64] |= 1UL << (flowEvent.Local % 64);
                break;
            case LivenessEventKind.Write:
                live[flowEvent.Local / 64] &= ~(1UL << (flowEvent.Local % 64));
                break;
        }
    }

    private static bool IsSet(ulong[] bits, int index) => (bits[index / 64] & (1UL << (index % 64))) != 0;
}
