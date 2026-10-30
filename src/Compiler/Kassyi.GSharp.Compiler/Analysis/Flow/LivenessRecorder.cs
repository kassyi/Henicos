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
/// </remarks>
internal sealed class LivenessRecorder : IFlowRecorder
{
    private readonly SemanticModel _model;
    private readonly Dictionary<ILocalSymbol, int> _indices = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ILocalSymbol> _carriers = new(SymbolEqualityComparer.Default);
    private readonly ImmutableArray<ImmutableArray<LivenessEvent>.Builder>.Builder _events =
        ImmutableArray.CreateBuilder<ImmutableArray<LivenessEvent>.Builder>();
    private readonly Dictionary<int, HashSet<int>> _carrierCaptures = new();
    private readonly ImmutableArray<BodyFrame>.Builder _frames = ImmutableArray.CreateBuilder<BodyFrame>();

    // The delegate body that the assignment being walked stores in a carrier, and the index of the carrier
    private (SyntaxNode Body, int Carrier)? _pendingCarrier;

    /// <param name="model">The semantic model of the method.</param>
    /// <param name="method">The syntax of the method, where the carriers are looked for.</param>
    public LivenessRecorder(SemanticModel model, SyntaxNode method)
    {
        _model = model;
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
    public int IndexOf(ILocalSymbol local) => _indices.TryGetValue(local, out var index) ? index : -1;

    public void OnNode(int node) => _events.Add(ImmutableArray.CreateBuilder<LivenessEvent>());

    public void OnLocalReference(ILocalReferenceOperation reference, int node) => AddRead(reference.Local, node);

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
                AddAssigned(target.Local, node, replaces: target.IsDeclaration || simple.IsRef || !target.Local.IsRef);
                break;
            case IDeconstructionAssignmentOperation deconstruction:
                AddDeconstructed(deconstruction.Target, node);
                break;
        }
    }

    public void OnIncrement(IIncrementOrDecrementOperation increment, ControlFlowGraph graph, int node)
    {
    }

    public void OnFlowCapture(IFlowCaptureOperation capture, ControlFlowGraph graph, int node)
    {
    }

    public void OnObjectCreation(IObjectCreationOperation creation, int node)
    {
    }

    public void OnCall(IInvocationOperation invocation, int node)
    {
    }

    // A split in an opaque body does not split the method's state machine; a Gpu.SyncGrid(body) splits in its gaps
    public void OnSplit(SplitPoint split, int node)
    {
        if (_frames.Count == 0 && split.Kind != SplitKind.SyncGridCall)
        {
            _events[node].Add(LivenessEvent.Split);
        }
    }

    // The callee writes an out argument when it returns, after any split
    public void OnCallReturned(IInvocationOperation invocation, ControlFlowGraph graph, int node)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument is { Parameter.RefKind: RefKind.Out, Value: ILocalReferenceOperation { Local: { IsRef: false } local } })
            {
                AddAssigned(local, node, replaces: true);
            }
        }
    }

    public void OnBodyEntered(SplicedBody body, int node)
    {
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

    public void OnTopOperation(IOperation operation, ControlFlowGraph graph, int node)
    {
    }

    // A read; in an opaque body only of a local declared outside it, which the body captures
    private void AddRead(ILocalSymbol local, int node)
    {
        if (IsDeclaredInFrame(local))
        {
            return;
        }

        var index = Index(local);
        _events[node].Add(LivenessEvent.Read(index));
        foreach (var frame in _frames)
        {
            frame.Captures.Add(index);
        }
    }

    // A local that a statement assigns: written, or read when the write goes through it (a ref local); in an opaque body
    // only captured, as the body may not run
    private void AddAssigned(ILocalSymbol local, int node, bool replaces)
    {
        if (_frames.Count > 0 || !replaces)
        {
            AddRead(local, node);
            return;
        }

        _events[node].Add(LivenessEvent.Write(Index(local)));
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
            AddAssigned(local, node, replaces: true);
        }
    }

    private bool IsDeclaredInFrame(ILocalSymbol local)
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

    private static bool IsDeclaredIn(ILocalSymbol local, SyntaxNode syntax) =>
        local.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(syntax, static (reference, scope) =>
                reference.SyntaxTree == scope.SyntaxTree && scope.Span.Contains(reference.Span)
            );

    private int Index(ILocalSymbol local)
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
