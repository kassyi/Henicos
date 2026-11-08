using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Records, for <see cref="CrossBarrierLiveness"/>, where each local of the analyzed method is read or written, where the
/// state machine splits, and which locals the delegates of the method keep (carriers and pins).
/// </summary>
/// <remarks>
/// <para>
/// A spliced lambda or local function body is opaque to the liveness: the state machine does not split it and it may run
/// again later, so a use inside it of a local declared outside it counts as a read where it is spliced, and nothing in it
/// writes such a local or splits. Locals declared inside it belong to it.
/// </para>
/// <para>
/// A delegate keeps the locals it captures: the CPU dialects store it in the frame and run it in a later state, where a
/// captured stackalloc pointer would refer to the memory of an earlier call (PR #771). A delegate stored in a local (a
/// carrier) that is only run or assigned is live as that local is, so its captures are live across every split it is
/// live across. Any other delegate (passed, copied, stored elsewhere, made inside another lambda or local function)
/// pins its captures where it is made: they are live across every split reachable from there. A body that runs where it
/// is spliced (a local function call, the body of a <c>Gpu</c> macro or a lambda passed to a StackLinq operator, which
/// the transpiler expands in place) is not a delegate, unless it makes one.
/// </para>
/// <para>
/// The events of a statement are placed where the lowered code runs them, not where C# evaluates them: the lowering
/// hoists a call that splits (a barrier, a shuffle, a call that may suspend) into a statement of its own in front of the
/// rest, and every resume runs a call that may suspend again (<see cref="WarpInstructionRewriter"/>). So a use inside
/// the call that is evaluated once before it (an argument the lowering spills) comes before its split, any other use
/// inside it (the receiver, an unspilled argument) right after its split, and the rest of the statement after its last
/// split (<c>s[Broadcast(0)]</c> reads <c>s</c> after the split).
/// </para>
/// </remarks>
internal sealed class LivenessRecorder : IFlowRecorder
{
    private readonly SemanticModel _model;
    private readonly Dictionary<ISymbol, int> _indices = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ILocalSymbol> _carriers = new(SymbolEqualityComparer.Default);
    private readonly ImmutableArray<ImmutableArray<LivenessEvent>.Builder>.Builder _events =
        ImmutableArray.CreateBuilder<ImmutableArray<LivenessEvent>.Builder>();
    private readonly Dictionary<int, HashSet<int>> _carrierCaptures = new();
    private readonly ImmutableArray<BodyFrame>.Builder _frames = ImmutableArray.CreateBuilder<BodyFrame>();
    private readonly FlowCaptureTable _flowCaptures = new();

    private (SyntaxNode Body, int Carrier)? _pendingCarrier;

    private readonly ImmutableArray<ImmutableArray<(LivenessEvent Event, SyntaxNode Syntax)>.Builder>.Builder _statements =
        ImmutableArray.CreateBuilder<ImmutableArray<(LivenessEvent Event, SyntaxNode Syntax)>.Builder>();

    private IInvocationOperation? _lastCall;
    private IInvocationOperation? _splitCall;

    /// <param name="model">The semantic model of the method.</param>
    /// <param name="method">The syntax of the method, where the carriers are looked for.</param>
    public LivenessRecorder(SemanticModel model, SyntaxNode method)
    {
        _model = model;
        _statements.Add(ImmutableArray.CreateBuilder<(LivenessEvent, SyntaxNode)>());
        DelegateCarrierScanner.ScanCarriers(model, method, _carriers);
    }

    /// <summary>The number of locals that have an index.</summary>
    public int LocalCount => _indices.Count;

    /// <summary>Whether some delegate pins its captures.</summary>
    public bool HasPins { get; private set; }

    /// <summary>The events of each node, by node index.</summary>
    public ImmutableArray<LivenessEvent>.Builder EventsOf(int node) => _events[node];

    /// <summary>For each carrier that receives a delegate with captures, by index, the indices of the captured locals.</summary>
    public IReadOnlyDictionary<int, HashSet<int>> CarrierCaptures => _carrierCaptures;

    /// <summary>The index of <paramref name="local"/>, or -1 when nothing reads or writes it.</summary>
    public int IndexOf(ISymbol local) => _indices.TryGetValue(local, out var index) ? index : -1;

    /// <summary>Adds a new event builder for the control flow graph node.</summary>
    public void OnNode(int node) => _events.Add(ImmutableArray.CreateBuilder<LivenessEvent>());

    /// <summary>Whether a tracked parameter may change before a split (a <see cref="LivenessEventKind.Change"/> event).</summary>
    public bool HasChanges { get; private set; }

    /// <summary>
    /// Whether the liveness follows <paramref name="parameter"/>: a by-value parameter of a ref struct type of the analyzed
    /// method (<c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, a user ref struct; not <c>SharedMemory&lt;T&gt;</c>, which
    /// the CPU dialects keep per declaration). The state machine cannot keep it in the thread's frame (GSH0039, #965).
    /// </summary>
    public static bool IsTrackedParameter(IParameterSymbol parameter) =>
        parameter is { RefKind: RefKind.None, Type.IsRefLikeType: true } &&
        parameter.ContainingSymbol is IMethodSymbol { MethodKind: not (MethodKind.AnonymousFunction or MethodKind.LocalFunction) } &&
        !parameter.Type.IsSharedMemoryType();

    /// <summary>Records a read of the local variable unless it is captured on the left of a simple assignment.</summary>
    public void OnLocalReference(ILocalReferenceOperation reference, int node)
    {
        if (!reference.Local.IsRef && IsCapturedSimpleAssignmentLeft(reference))
        {
            return;
        }

        AddRead(reference.Local, reference.Syntax, node);
    }

    /// <summary>Records a read and possible change of a tracked parameter.</summary>
    public void OnParameterReference(IParameterReferenceOperation reference, int node)
    {
        if (!IsTrackedParameter(reference.Parameter) || IsWrittenWhole(reference))
        {
            return;
        }

        AddRead(reference.Parameter, reference.Syntax, node);
        if (ParameterChanges.MayChange(reference))
        {
            AddChange(Index(reference.Parameter), reference.Syntax, node);
        }
    }

    /// <summary>Records reads and changes caused by referencing a captured flow target.</summary>
    public void OnFlowCaptureReference(IFlowCaptureReferenceOperation reference, int node)
    {
        if (_flowCaptures.TryGetReads(reference, out var reads) && reads != null)
        {
            foreach (var symbol in reads)
            {
                AddRead(symbol, reference.Syntax, node);
            }
        }

        _flowCaptures.CollectParameterChanges(reference, (paramIndex, syntax) => AddChange(paramIndex, syntax, node));
    }

    /// <summary>Inspects the assignment value before it is evaluated to identify carrier storage.</summary>
    public void BeforeAssignmentValue(IAssignmentOperation assignment, int node)
    {
        _pendingCarrier = _frames.Count == 0 &&
                          assignment is ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } &&
                          _carriers.Contains(target.Local) &&
                          DelegateCarrierScanner.GetDelegateBody(assignment.Value) is { } body
            ? (body, Index(target.Local))
            : null;
    }

    /// <summary>Records assignment writes, parameter mutations, or deconstruction targets.</summary>
    public void OnAssignment(IAssignmentOperation assignment, ControlFlowGraph graph, int node)
    {
        _pendingCarrier = null;
        switch (assignment)
        {
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } simple:
                AddAssigned(target.Local, target.Syntax, node,
                    replaces: target.IsDeclaration || simple.IsRef || !target.Local.IsRef);
                break;
            case ISimpleAssignmentOperation { Target: var target } simple
                when _flowCaptures.TryGetWholeLocal(target) is { } local:
                AddAssigned(local, target.Syntax, node, replaces: simple.IsRef || !local.IsRef);
                break;
            case ISimpleAssignmentOperation { Target: var target }
                when _flowCaptures.IndexOfWholeParameter(target, Index) is var index and >= 0:
                AddParameterAssigned(index, target.Syntax, node);
                break;
            case IDeconstructionAssignmentOperation deconstruction:
                AddDeconstructed(deconstruction.Target, node);
                break;
        }
    }

    /// <summary>Invoked for increment/decrement operations.</summary>
    public void OnIncrement(IIncrementOrDecrementOperation increment, ControlFlowGraph graph, int node)
    {
    }

    /// <summary>Registers a flow capture of a receiver, element location or variable.</summary>
    public void OnFlowCapture(IFlowCaptureOperation capture, ControlFlowGraph graph, int node) =>
        _flowCaptures.Register(capture, Index, IsDeclaredInFrame);

    /// <summary>Invoked for object creations.</summary>
    public void OnObjectCreation(IObjectCreationOperation creation, int node)
    {
    }

    /// <summary>Records the last invocation operation seen in the walk.</summary>
    public void OnCall(IInvocationOperation invocation, int node) => _lastCall = invocation;

    /// <summary>Places statement events around a split point or records a barrier split.</summary>
    public void OnSplit(SplitPoint split, int node)
    {
        if (_frames.Count != 0 || split.Kind == SplitKind.SyncGridCall)
        {
            return;
        }

        if (split.Kind == SplitKind.Call && _lastCall is { } call && call.Syntax == split.Syntax)
        {
            PlaceAroundSplit(call, node);
            _splitCall = call;
            return;
        }

        _events[node].Add(LivenessEvent.Split);
    }

    /// <summary>Records out parameter assignment writes after a call returns.</summary>
    public void OnCallReturned(IInvocationOperation invocation, ControlFlowGraph graph, int node)
    {
        var syntax = invocation == _splitCall ? null : invocation.Syntax;
        foreach (var argument in invocation.Arguments)
        {
            if (argument is { Parameter.RefKind: RefKind.Out, Value: ILocalReferenceOperation { Local: { IsRef: false } local } })
            {
                AddAssigned(local, syntax, node, replaces: true);
            }
        }
    }

    /// <summary>Flushes statement events before entering an opaque or spliced body and pushes a body frame.</summary>
    public void OnBodyEntered(SplicedBody body, int node)
    {
        Flush(node);
        _statements.Add(ImmutableArray.CreateBuilder<(LivenessEvent, SyntaxNode)>());
        if (!body.IsOpaque)
        {
            return;
        }

        var carrier = _pendingCarrier is { } pending && pending.Body == body.Syntax ? pending.Carrier : -1;
        _pendingCarrier = null;
        var mode = body.Kind == SplicedBodyKind.LocalFunctionCall ||
                   (body.Kind == SplicedBodyKind.Lambda && DelegateCarrierScanner.RunsInPlace(body.Syntax, _model))
            ? BodyMode.Run
            : carrier >= 0 ? BodyMode.Carried : BodyMode.Delegate;
        if (mode != BodyMode.Run)
        {
            foreach (var frame in _frames)
            {
                frame.MakesDelegate = true;
            }
        }

        _frames.Add(new BodyFrame(body.Syntax, mode, carrier));
    }

    /// <summary>Flushes body events, pops the body frame, and records carried or pinned captures.</summary>
    public void OnBodyExited(SplicedBody body, int node)
    {
        Flush(node);
        _statements.RemoveAt(_statements.Count - 1);
        if (!body.IsOpaque)
        {
            return;
        }

        var frame = _frames[_frames.Count - 1];
        _frames.RemoveAt(_frames.Count - 1);
        if (frame.Mode == BodyMode.Carried && !frame.MakesDelegate)
        {
            RecordCarriedCaptures(frame);
            return;
        }

        if (frame.Mode != BodyMode.Run || frame.MakesDelegate)
        {
            PinCaptures(frame, node);
        }
    }

    /// <summary>Records captures transferred to a delegate carrier local.</summary>
    private void RecordCarriedCaptures(BodyFrame frame)
    {
        if (!_carrierCaptures.TryGetValue(frame.Carrier, out var captures))
        {
            captures = [];
            _carrierCaptures.Add(frame.Carrier, captures);
        }

        captures.UnionWith(frame.Captures);
    }

    /// <summary>Pins captures that may survive across future split points.</summary>
    private void PinCaptures(BodyFrame frame, int node)
    {
        foreach (var captured in frame.Captures)
        {
            _events[node].Add(LivenessEvent.Pin(captured));
            HasPins = true;
        }
    }

    /// <summary>The value was walked as a top operation; returning it changes no local.</summary>
    public void OnBodyReturn(IOperation value, ControlFlowGraph graph, int node)
    {
    }

    /// <summary>Flushes the remaining statement events after a top operation ends.</summary>
    public void OnTopOperation(IOperation operation, ControlFlowGraph graph, int node) => Flush(node);

    /// <summary>Places statement events around an invocation split point.</summary>
    private void PlaceAroundSplit(IInvocationOperation call, int node)
    {
        var statement = _statements[_statements.Count - 1];
        var after = ImmutableArray.CreateBuilder<LivenessEvent>();
        var kept = 0;
        for (var i = 0; i < statement.Count; i++)
        {
            var (flowEvent, syntax) = statement[i];
            if (syntax.SyntaxTree != call.Syntax.SyntaxTree || !call.Syntax.Span.Contains(syntax.Span))
            {
                statement[kept++] = (flowEvent, syntax);
            }
            else if (flowEvent.Kind == LivenessEventKind.Change || IsEvaluatedBeforeSplit(call, syntax))
            {
                _events[node].Add(flowEvent);
            }
            else
            {
                after.Add(flowEvent);
            }
        }

        statement.Count = kept;
        _events[node].Add(LivenessEvent.Split);
        _events[node].AddRange(after);
    }

    /// <summary>Whether a syntax use inside call is evaluated once before the call splits via SpilledArguments.</summary>
    private bool IsEvaluatedBeforeSplit(IInvocationOperation call, SyntaxNode syntax)
    {
        if (call.Syntax is not InvocationExpressionSyntax invocation)
        {
            return true;
        }

        var arguments = invocation.ArgumentList.Arguments;
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Span.Contains(syntax.Span))
            {
                return SpilledArguments.IsPassedByReference(arguments[i], _model)
                    ? SpilledArguments.IsInSpilledLocationPart(arguments[i].Expression, syntax, arguments, i, _model)
                    : SpilledArguments.IsSpilledArgument(arguments, i, _model);
            }
        }

        return false;
    }

    /// <summary>Flushes statement events to the current node event list.</summary>
    private void Flush(int node)
    {
        var statement = _statements[_statements.Count - 1];
        foreach (var (flowEvent, _) in statement)
        {
            _events[node].Add(flowEvent);
        }

        statement.Clear();
    }

    /// <summary>Adds a liveness event to the current statement or node builder.</summary>
    private void Add(LivenessEvent flowEvent, SyntaxNode? syntax, int node)
    {
        if (_frames.Count == 0 && syntax != null)
        {
            _statements[_statements.Count - 1].Add((flowEvent, syntax));
            return;
        }

        _events[node].Add(flowEvent);
    }

    /// <summary>Records a read of local or tracked parameter.</summary>
    private void AddRead(ISymbol local, SyntaxNode? syntax, int node)
    {
        if (IsDeclaredInFrame(local))
        {
            return;
        }

        var index = Index(local);
        Add(LivenessEvent.Read(index), syntax, node);
        foreach (var frame in _frames)
        {
            frame.Captures.Add(index);
        }
    }

    /// <summary>Records an assignment write to a local variable.</summary>
    private void AddAssigned(ISymbol local, SyntaxNode? syntax, int node, bool replaces)
    {
        if (_frames.Count > 0 || !replaces)
        {
            AddRead(local, syntax, node);
            return;
        }

        Add(LivenessEvent.Write(Index(local)), syntax, node);
    }

    /// <summary>Records deconstructed assignment elements using a switch statement.</summary>
    private void AddDeconstructed(IOperation target, int node)
    {
        switch (target)
        {
            case ITupleOperation tuple:
                foreach (var element in tuple.Elements)
                {
                    AddDeconstructed(element, node);
                }
                break;
            case ILocalReferenceOperation { Local: { IsRef: false } local }:
                AddAssigned(local, target.Syntax, node, replaces: true);
                break;
            case var _ when _flowCaptures.TryGetWholeLocal(target) is { IsRef: false } capturedLocal:
                AddAssigned(capturedLocal, target.Syntax, node, replaces: true);
                break;
            case var _ when _flowCaptures.IndexOfWholeParameter(target, Index) is var index and >= 0:
                AddParameterAssigned(index, target.Syntax, node);
                break;
        }
    }

    /// <summary>Records a tracked parameter assigned as a whole: written and changed.</summary>
    private void AddParameterAssigned(int index, SyntaxNode syntax, int node)
    {
        if (_frames.Count > 0)
        {
            _events[node].Add(LivenessEvent.Read(index));
            return;
        }

        Add(LivenessEvent.Write(index), syntax, node);
        AddChange(index, syntax, node);
    }

    /// <summary>Records a change event for a tracked parameter.</summary>
    private void AddChange(int index, SyntaxNode syntax, int node)
    {
        Add(LivenessEvent.Change(index), syntax, node);
        HasChanges = true;
    }

    /// <summary>Whether reference is the whole target of an assignment or deconstruction.</summary>
    private static bool IsWrittenWhole(IParameterReferenceOperation reference) => reference.Parent switch
    {
        ISimpleAssignmentOperation assignment => assignment.Target == reference,
        ITupleOperation tuple => ParameterChanges.IsDeconstructionTarget(tuple),
        _ => IsCapturedSimpleAssignmentLeft(reference)
    };

    /// <summary>Whether reference is a whole variable captured on the left of a simple assignment.</summary>
    private static bool IsCapturedSimpleAssignmentLeft(IOperation reference) =>
        reference.Parent is IFlowCaptureOperation &&
        reference.Syntax.Parent is AssignmentExpressionSyntax
        {
            RawKind: (int)SyntaxKind.SimpleAssignmentExpression
        } assignment &&
        assignment.Left == reference.Syntax;

    /// <summary>Whether local is declared inside any currently active body frame.</summary>
    private bool IsDeclaredInFrame(ISymbol local)
    {
        foreach (var frame in _frames)
        {
            if (IsDeclaredIn(local, frame.Body))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether local is declared within the span of scope syntax.</summary>
    private static bool IsDeclaredIn(ISymbol local, SyntaxNode syntax) =>
        local.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(syntax, static (reference, scope) =>
                reference.SyntaxTree == scope.SyntaxTree && scope.Span.Contains(reference.Span)
            );

    /// <summary>Retrieves or assigns an integer index for local or parameter symbol.</summary>
    private int Index(ISymbol local)
    {
        if (!_indices.TryGetValue(local, out var index))
        {
            index = _indices.Count;
            _indices.Add(local, index);
        }

        return index;
    }

    /// <summary>Execution mode of a body frame.</summary>
    private enum BodyMode
    {
        /// <summary>Runs where it is spliced.</summary>
        Run,

        /// <summary>A delegate stored in a carrier local.</summary>
        Carried,

        /// <summary>A delegate that may run after any later split.</summary>
        Delegate
    }

    /// <summary>An opaque body frame tracking captured locals and delegate creation state.</summary>
    private sealed class BodyFrame(SyntaxNode body, BodyMode mode, int carrier)
    {
        public SyntaxNode Body { get; } = body;

        public BodyMode Mode { get; } = mode;

        public int Carrier { get; } = carrier;

        public HashSet<int> Captures { get; } = [];

        public bool MakesDelegate { get; set; }
    }
}
