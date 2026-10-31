using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;
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
/// the call that is evaluated once before it (an argument the lowering spills, the value a shuffle publishes) comes
/// before its split, any other use inside it (the receiver, an identifier argument) right after its split, and the rest
/// of the statement after its last split (<c>s[Broadcast(0)]</c> reads <c>s</c> after the split).
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

    // The flow captures of a tracked parameter, of one of its fields or of one of its properties, by capture: the index of
    // the parameter, whether the capture is the whole parameter, and the property
    private readonly Dictionary<CaptureId, (int Parameter, bool IsWhole, IPropertySymbol? Property)> _parameterCaptures =
        new();

    // The delegate body that the assignment being walked stores in a carrier, and the index of the carrier
    private (SyntaxNode Body, int Carrier)? _pendingCarrier;

    // The events of the statement being walked, with the syntax that causes each, until a split places those inside its
    // call or the statement ends; one list per spliced body being walked
    private readonly ImmutableArray<ImmutableArray<(LivenessEvent Event, SyntaxNode Syntax)>.Builder>.Builder _statements =
        ImmutableArray.CreateBuilder<ImmutableArray<(LivenessEvent Event, SyntaxNode Syntax)>.Builder>();

    // The last call that OnCall saw (the one a split of kind Call is at), and the last call that split
    private IInvocationOperation? _lastCall;
    private IInvocationOperation? _splitCall;

    /// <param name="model">The semantic model of the method.</param>
    /// <param name="method">The syntax of the method, where the carriers are looked for.</param>
    public LivenessRecorder(SemanticModel model, SyntaxNode method)
    {
        _model = model;
        _statements.Add(ImmutableArray.CreateBuilder<(LivenessEvent, SyntaxNode)>());
        AddCarriers(method);
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

    public void OnLocalReference(ILocalReferenceOperation reference, int node) =>
        AddRead(reference.Local, reference.Syntax, node);

    // A use of a tracked parameter reads it and may change it; assigning it as a whole is left to OnAssignment
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

    // A use of a captured tracked parameter, or of a captured field of one, may change it
    public void OnFlowCaptureReference(IFlowCaptureReferenceOperation reference, int node)
    {
        if (_parameterCaptures.TryGetValue(reference.Id, out var captured) &&
            (captured.Property is { } property
                ? ParameterChanges.MayChangeThroughCapturedProperty(reference, property)
                : ParameterChanges.MayChange(reference)))
        {
            AddChange(captured.Parameter, reference.Syntax, node);
        }
    }

    public void BeforeAssignmentValue(IAssignmentOperation assignment, int node)
    {
        _pendingCarrier = _frames.Count == 0 &&
                          assignment is ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } &&
                          _carriers.Contains(target.Local) &&
                          GetDelegateBody(assignment.Value) is { } body
            ? (body, Index(target.Local))
            : null;
    }

    public void OnAssignment(IAssignmentOperation assignment, ControlFlowGraph graph, int node)
    {
        _pendingCarrier = null;
        switch (assignment)
        {
            // Assigning through a ref local uses the reference; declaring or rebinding it replaces it
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } simple:
                AddAssigned(target.Local, target.Syntax, node,
                    replaces: target.IsDeclaration || simple.IsRef || !target.Local.IsRef);
                break;
            case ISimpleAssignmentOperation { Target: var target } when IndexOfWholeParameter(target) is var index and >= 0:
                AddParameterAssigned(index, target.Syntax, node);
                break;
            case IDeconstructionAssignmentOperation deconstruction:
                AddDeconstructed(deconstruction.Target, node);
                break;
        }
    }

    public void OnIncrement(IIncrementOrDecrementOperation increment, ControlFlowGraph graph, int node)
    {
    }

    // A capture of a tracked parameter, of one of its fields or of one of its properties: a target that the graph spills
    // before a conditional value (p = c ? a : b, p.X = c ? a : b, p.P = c ? a : b); the walk of its value read the parameter
    public void OnFlowCapture(IFlowCaptureOperation capture, ControlFlowGraph graph, int node)
    {
        if (ParameterChanges.RootParameter(capture.Value, out var isWhole, out var property) is { } parameter &&
            IsTrackedParameter(parameter))
        {
            _parameterCaptures[capture.Id] = (Index(parameter), isWhole, property);
        }
    }

    public void OnObjectCreation(IObjectCreationOperation creation, int node)
    {
    }

    public void OnCall(IInvocationOperation invocation, int node) => _lastCall = invocation;

    // A split in an opaque body does not split the method's state machine; a Gpu.SyncGrid(body) splits in its gaps. A call
    // that splits places the events of the statement that lie inside it around its split.
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

    // The callee writes an out argument when it returns: a call that splits after its split, any other call where the
    // statement runs it
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

    // The events of the statement before a body stay before it; the body's statements have their own
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
                   (body.Kind == SplicedBodyKind.Lambda && RunsInPlace(body.Syntax))
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

    // A body that makes a delegate, or is a delegate that no carrier keeps, pins its captures here
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
            if (!_carrierCaptures.TryGetValue(frame.Carrier, out var captures))
            {
                captures = [];
                _carrierCaptures.Add(frame.Carrier, captures);
            }

            captures.UnionWith(frame.Captures);
            return;
        }

        if (frame.Mode == BodyMode.Run && !frame.MakesDelegate)
        {
            return;
        }

        foreach (var captured in frame.Captures)
        {
            _events[node].Add(LivenessEvent.Pin(captured));
            HasPins = true;
        }
    }

    // The rest of the statement runs after its last split
    public void OnTopOperation(IOperation operation, ControlFlowGraph graph, int node) => Flush(node);

    // The events of the statement that lie inside call: those the lowering evaluates once before the call, the split, then
    // those that the call runs again on every resume; the others stay with the statement
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
            else if (IsEvaluatedBeforeSplit(call, syntax))
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

    // Whether a use at syntax inside call is evaluated once before the call splits: in an argument or a part of a
    // by-reference argument that the lowering spills to a temporary (SpilledArguments, the rule the lowering spills by),
    // in the value that a shuffle publishes, or in the arguments of a barrier
    private bool IsEvaluatedBeforeSplit(IInvocationOperation call, SyntaxNode syntax)
    {
        var intrinsic = SyncIntrinsicClassifier.Classify(call.TargetMethod);
        if (intrinsic.Kind == SyncIntrinsicKind.WarpShuffle)
        {
            return call.Arguments
                .AsStackEnumerable()
                .Any(syntax, static (argument, use) =>
                    argument is { IsImplicit: false, Parameter.Ordinal: ShuffleValueOrdinal } && argument.Syntax.Span.Contains(use.Span)
                );
        }

        if (intrinsic.Kind != SyncIntrinsicKind.None || call.Syntax is not InvocationExpressionSyntax invocation)
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

    // The events of the statement that no split has placed, where the statement runs them
    private void Flush(int node)
    {
        var statement = _statements[_statements.Count - 1];
        foreach (var (flowEvent, _) in statement)
        {
            _events[node].Add(flowEvent);
        }

        statement.Clear();
    }

    // An event of the statement being walked, caused by syntax; in an opaque body, which does not split, or without a
    // syntax, where it happens
    private void Add(LivenessEvent flowEvent, SyntaxNode? syntax, int node)
    {
        if (_frames.Count == 0 && syntax != null)
        {
            _statements[_statements.Count - 1].Add((flowEvent, syntax));
            return;
        }

        _events[node].Add(flowEvent);
    }

    // A read; in an opaque body only of a local declared outside it, which the body captures
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

    // A local that a statement assigns: written, or read when the write goes through it (a ref local); in an opaque body
    // only captured, as the body may not run
    private void AddAssigned(ISymbol local, SyntaxNode? syntax, int node, bool replaces)
    {
        if (_frames.Count > 0 || !replaces)
        {
            AddRead(local, syntax, node);
            return;
        }

        Add(LivenessEvent.Write(Index(local)), syntax, node);
    }

    // (a, span) = <value>: an element that is a local itself is written, any other element was walked as a target
    private void AddDeconstructed(IOperation target, int node)
    {
        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                AddDeconstructed(element, node);
            }

            return;
        }

        if (target is ILocalReferenceOperation { Local: { IsRef: false } local })
        {
            AddAssigned(local, target.Syntax, node, replaces: true);
        }
        else if (target is IParameterReferenceOperation { Parameter: var parameter } && IsTrackedParameter(parameter))
        {
            AddParameterAssigned(Index(parameter), target.Syntax, node);
        }
    }

    // A tracked parameter assigned as a whole: written and changed; in an opaque body only captured
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

    private void AddChange(int index, SyntaxNode syntax, int node)
    {
        Add(LivenessEvent.Change(index), syntax, node);
        HasChanges = true;
    }

    // The tracked parameter that reference names as a whole, directly or through a flow capture; -1 for any other
    // operation
    private int IndexOfWholeParameter(IOperation reference) => reference switch
    {
        IParameterReferenceOperation { Parameter: var parameter } when IsTrackedParameter(parameter) => Index(parameter),
        IFlowCaptureReferenceOperation capture
            when _parameterCaptures.TryGetValue(capture.Id, out var captured) && captured.IsWhole => captured.Parameter,
        _ => -1
    };

    // p = <value>, (p, x) = <value>: the parameter itself is the target, which OnAssignment writes
    private static bool IsWrittenWhole(IParameterReferenceOperation reference) => reference.Parent switch
    {
        ISimpleAssignmentOperation assignment => assignment.Target == reference,
        ITupleOperation tuple => ParameterChanges.IsDeconstructionTarget(tuple),
        _ => false
    };

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

    private static bool IsDeclaredIn(ISymbol local, SyntaxNode syntax) =>
        local.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(syntax, static (reference, scope) =>
                reference.SyntaxTree == scope.SyntaxTree && scope.Span.Contains(reference.Span)
            );

    private int Index(ISymbol local)
    {
        if (!_indices.TryGetValue(local, out var index))
        {
            index = _indices.Count;
            _indices.Add(local, index);
        }

        return index;
    }

    // The carriers of method: the delegate locals that are only run (f(), f.Invoke()) or assigned (f = <value>), and not
    // named inside a lambda that does not run in place or inside a local function
    private void AddCarriers(SyntaxNode method)
    {
        var candidates = ImmutableArray.CreateBuilder<ILocalSymbol>();
        var rejected = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        foreach (var node in method.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax declarator
                    when _model.GetDeclaredSymbol<ILocalSymbol>(declarator) is { IsRef: false, Type.TypeKind: TypeKind.Delegate } local:
                    candidates.Add(local);
                    break;
                case IdentifierNameSyntax name
                    when _model.GetSymbol(name) is ILocalSymbol { Type.TypeKind: TypeKind.Delegate } local &&
                         (!IsRunOrAssigned(name) || IsInsideFunction(name, method)):
                    rejected.Add(local);
                    break;
            }
        }

        foreach (var candidate in candidates)
        {
            if (!rejected.Contains(candidate))
            {
                _carriers.Add(candidate);
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

    // Whether node lies in a lambda of method that does not run in place, or in a local function of method
    private bool IsInsideFunction(SyntaxNode node, SyntaxNode method)
    {
        for (var ancestor = node.Parent; ancestor != null && ancestor != method; ancestor = ancestor.Parent)
        {
            if (ancestor is LocalFunctionStatementSyntax ||
                (ancestor is AnonymousFunctionExpressionSyntax && !RunsInPlace(ancestor)))
            {
                return true;
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

    // The lambda or local function that makes the delegate value is: its syntax, or null for any other value
    private static SyntaxNode? GetDelegateBody(IOperation value) => value switch
    {
        IFlowAnonymousFunctionOperation lambda => lambda.Syntax,
        IDelegateCreationOperation creation => GetDelegateBody(creation.Target),
        IConversionOperation conversion => GetDelegateBody(conversion.Operand),
        IMethodReferenceOperation { Method: { MethodKind: MethodKind.LocalFunction } method }
            when method.OriginalDefinition.TryGetDeclaringSyntax(out LocalFunctionStatementSyntax? localFunction) =>
            localFunction,
        _ => null
    };

    // The parameter of a warp shuffle that holds the value it publishes (mask, var, lane, width)
    private const int ShuffleValueOrdinal = 1;

    private enum BodyMode
    {
        // Runs where it is spliced
        Run,

        // A delegate that a carrier keeps
        Carried,

        // A delegate that may run after any later split
        Delegate
    }

    // An opaque body being spliced: the locals of the method it captures, and whether it makes a delegate
    private sealed class BodyFrame(SyntaxNode body, BodyMode mode, int carrier)
    {
        public SyntaxNode Body { get; } = body;

        public BodyMode Mode { get; } = mode;

        public int Carrier { get; } = carrier;

        public HashSet<int> Captures { get; } = [];

        public bool MakesDelegate { get; set; }
    }
}
