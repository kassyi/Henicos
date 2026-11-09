using Microsoft.CodeAnalysis;
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
/// <para>
/// The events are kept in <see cref="LivenessEventLog"/>, the opaque bodies in <see cref="LivenessFrames"/>; the
/// placement around a split is <see cref="LivenessSplitPlacement"/>'s and the writes of an assignment
/// <see cref="LivenessAssignments"/>'s.
/// </para>
/// </remarks>
internal sealed class LivenessRecorder : IFlowRecorder
{
    private readonly SemanticModel _model;
    private readonly HashSet<ILocalSymbol> _carriers = new(SymbolEqualityComparer.Default);
    private readonly LivenessFrames _frames = new();
    private readonly LivenessEventLog _log;
    private readonly FlowCaptureTable _flowCaptures = new();

    private (SyntaxNode Body, int Carrier)? _pendingCarrier;

    private IInvocationOperation? _lastCall;
    private IInvocationOperation? _splitCall;

    /// <param name="model">The semantic model of the method.</param>
    /// <param name="method">The syntax of the method, where the carriers are looked for.</param>
    public LivenessRecorder(SemanticModel model, SyntaxNode method)
    {
        _model = model;
        _log = new LivenessEventLog(_frames);
        _log.PushStatement();
        DelegateCarrierScanner.ScanCarriers(model, method, _carriers);
    }

    /// <summary>The number of locals that have an index.</summary>
    public int LocalCount => _log.LocalCount;

    /// <summary>Whether some delegate pins its captures.</summary>
    public bool HasPins => _log.HasPins;

    /// <summary>The events of each node, by node index.</summary>
    public ImmutableArray<LivenessEvent>.Builder EventsOf(int node) => _log.EventsOf(node);

    /// <summary>
    /// For each carrier that receives a delegate with captures, by index, the indices of the captured locals.
    /// </summary>
    public IReadOnlyDictionary<int, HashSet<int>> CarrierCaptures => _frames.CarrierCaptures;

    /// <summary>The index of <paramref name="local"/>, or -1 when nothing reads or writes it.</summary>
    public int IndexOf(ISymbol local) => _log.IndexOf(local);

    /// <summary>Adds a new event builder for the control flow graph node.</summary>
    public void OnNode(int node) => _log.AddNode();

    /// <summary>
    /// Whether a tracked parameter may change before a split (a <see cref="LivenessEventKind.Change"/> event).
    /// </summary>
    public bool HasChanges => _log.HasChanges;

    /// <summary>
    /// Whether the liveness follows <paramref name="parameter"/>: a by-value parameter of a ref struct type of the
    /// analyzed method (<c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, a user ref struct; not
    /// <c>SharedMemory&lt;T&gt;</c>, which the CPU dialects keep per declaration). The state machine cannot keep it in
    /// the thread's frame (GSH0039, #965).
    /// </summary>
    public static bool IsTrackedParameter(IParameterSymbol parameter) =>
        parameter is { RefKind: RefKind.None, Type.IsRefLikeType: true } &&
        parameter.ContainingSymbol is IMethodSymbol
        {
            MethodKind: not (MethodKind.AnonymousFunction or MethodKind.LocalFunction)
        } &&
        !parameter.Type.IsSharedMemoryType();

    /// <summary>Records a read of the local variable unless it is captured on the left of a simple assignment.</summary>
    public void OnLocalReference(ILocalReferenceOperation reference, int node)
    {
        if (!reference.Local.IsRef && LivenessAssignments.IsCapturedSimpleAssignmentLeft(reference))
        {
            return;
        }

        _log.AddRead(reference.Local, reference.Syntax, node);
    }

    /// <summary>Records a read and possible change of a tracked parameter.</summary>
    public void OnParameterReference(IParameterReferenceOperation reference, int node)
    {
        if (!IsTrackedParameter(reference.Parameter) || LivenessAssignments.IsWrittenWhole(reference))
        {
            return;
        }

        _log.AddRead(reference.Parameter, reference.Syntax, node);
        if (ParameterChanges.MayChange(reference))
        {
            _log.AddChange(_log.Index(reference.Parameter), reference.Syntax, node);
        }
    }

    /// <summary>Records reads and changes caused by referencing a captured flow target.</summary>
    public void OnFlowCaptureReference(IFlowCaptureReferenceOperation reference, int node)
    {
        if (_flowCaptures.TryGetReads(reference, out var reads) && reads != null)
        {
            foreach (var symbol in reads)
            {
                _log.AddRead(symbol, reference.Syntax, node);
            }
        }

        _flowCaptures.CollectParameterChanges(reference,
            (paramIndex, syntax) => _log.AddChange(paramIndex, syntax, node));
    }

    /// <summary>Inspects the assignment value before it is evaluated to identify carrier storage.</summary>
    public void BeforeAssignmentValue(IAssignmentOperation assignment, int node)
    {
        _pendingCarrier = _frames.Count == 0 &&
                          assignment is ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } &&
                          _carriers.Contains(target.Local) &&
                          DelegateCarrierScanner.GetDelegateBody(assignment.Value) is { } body
            ? (body, _log.Index(target.Local))
            : null;
    }

    /// <summary>Records assignment writes, parameter mutations, or deconstruction targets.</summary>
    public void OnAssignment(IAssignmentOperation assignment, ControlFlowGraph graph, int node)
    {
        _pendingCarrier = null;
        LivenessAssignments.Record(_log, _flowCaptures, assignment, node);
    }

    /// <summary>Invoked for increment/decrement operations.</summary>
    public void OnIncrement(IIncrementOrDecrementOperation increment, ControlFlowGraph graph, int node)
    {
    }

    /// <summary>Registers a flow capture of a receiver, element location or variable.</summary>
    public void OnFlowCapture(IFlowCaptureOperation capture, ControlFlowGraph graph, int node) =>
        _flowCaptures.Register(capture, _log.Index, _frames.IsDeclaredInFrame);

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
            LivenessSplitPlacement.PlaceAroundSplit(_log, _model, call, node);
            _splitCall = call;
            return;
        }

        _log.EventsOf(node).Add(LivenessEvent.Split);
    }

    /// <summary>
    /// Records the writes of the <c>out</c> arguments after a call returns: a local, or a tracked parameter, which is
    /// written whole and changed (#1003).
    /// </summary>
    public void OnCallReturned(IInvocationOperation invocation, ControlFlowGraph graph, int node)
    {
        var syntax = invocation == _splitCall ? null : invocation.Syntax;
        foreach (var argument in invocation.Arguments)
        {
            switch (argument)
            {
                case
                {
                    Parameter.RefKind: RefKind.Out, Value: ILocalReferenceOperation { Local: { IsRef: false } local }
                }:
                    _log.AddAssigned(local, syntax, node, replaces: true);
                    break;
                case { Parameter.RefKind: RefKind.Out, Value: IParameterReferenceOperation { Parameter: var parameter } }
                    when IsTrackedParameter(parameter):
                    _log.AddParameterAssigned(_log.Index(parameter), syntax, node);
                    break;
            }
        }
    }

    /// <summary>Flushes statement events before entering an opaque or spliced body and pushes a body frame.</summary>
    public void OnBodyEntered(SplicedBody body, int node)
    {
        _log.Flush(node);
        _log.PushStatement();
        if (!body.IsOpaque)
        {
            return;
        }

        var carrier = _pendingCarrier is { } pending && pending.Body == body.Syntax ? pending.Carrier : -1;
        _pendingCarrier = null;
        var mode = body.Kind == SplicedBodyKind.LocalFunctionCall ||
                   (body.Kind == SplicedBodyKind.Lambda && DelegateCarrierScanner.RunsInPlace(body.Syntax, _model))
            ? LivenessBodyMode.Run
            : carrier >= 0 ? LivenessBodyMode.Carried : LivenessBodyMode.Delegate;
        _frames.Push(new LivenessBodyFrame(body.Syntax, mode, carrier));
    }

    /// <summary>Flushes body events, pops the body frame, and records carried or pinned captures.</summary>
    public void OnBodyExited(SplicedBody body, int node)
    {
        _log.Flush(node);
        _log.PopStatement();
        if (!body.IsOpaque)
        {
            return;
        }

        var frame = _frames.Pop();
        if (frame.Mode == LivenessBodyMode.Carried && !frame.MakesDelegate)
        {
            _frames.RecordCarriedCaptures(frame);
            return;
        }

        if (frame.Mode != LivenessBodyMode.Run || frame.MakesDelegate)
        {
            _log.AddPins(frame, node);
        }
    }

    /// <summary>The value was walked as a top operation; returning it changes no local.</summary>
    public void OnBodyReturn(IOperation value, ControlFlowGraph graph, int node)
    {
    }

    /// <summary>Flushes the remaining statement events after a top operation ends.</summary>
    public void OnTopOperation(IOperation operation, ControlFlowGraph graph, int node) => _log.Flush(node);
}
