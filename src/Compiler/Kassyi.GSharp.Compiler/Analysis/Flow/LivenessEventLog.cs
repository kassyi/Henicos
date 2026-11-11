namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The events that <see cref="LivenessRecorder"/> records: the index of each local, the events of each node, and the
/// events of the statement being walked, held until its splits are placed.
/// </summary>
/// <param name="frames">The opaque bodies the walk is inside; a use inside one is a read where it is spliced.</param>
internal sealed class LivenessEventLog(LivenessFrames frames)
{
    private readonly Dictionary<ISymbol, int> _indices = new(SymbolEqualityComparer.Default);

    private readonly ImmutableArray<ImmutableArray<LivenessEvent>.Builder>.Builder _events =
        ImmutableArray.CreateBuilder<ImmutableArray<LivenessEvent>.Builder>();

    // The events of the statement being walked, one list per spliced body level, with the syntax that caused each
    private readonly ImmutableArray<ImmutableArray<(LivenessEvent Event, SyntaxNode Syntax)>.Builder>.Builder
        _statements = ImmutableArray.CreateBuilder<ImmutableArray<(LivenessEvent Event, SyntaxNode Syntax)>.Builder>();

    /// <summary>The number of locals that have an index.</summary>
    public int LocalCount => _indices.Count;

    /// <summary>Whether some delegate pins its captures.</summary>
    public bool HasPins { get; private set; }

    /// <summary>
    /// Whether a tracked parameter may change before a split (a <see cref="LivenessEventKind.Change"/> event).
    /// </summary>
    public bool HasChanges { get; private set; }

    /// <summary>The events of the statement being walked at the current body level.</summary>
    public ImmutableArray<(LivenessEvent Event, SyntaxNode Syntax)>.Builder Statement =>
        _statements[_statements.Count - 1];

    /// <summary>The events of each node, by node index.</summary>
    public ImmutableArray<LivenessEvent>.Builder EventsOf(int node) => _events[node];

    /// <summary>The index of <paramref name="local"/>, or -1 when nothing reads or writes it.</summary>
    public int IndexOf(ISymbol local) => _indices.TryGetValue(local, out var index) ? index : -1;

    /// <summary>The index of <paramref name="local"/>, given one on first use.</summary>
    public int Index(ISymbol local)
    {
        if (!_indices.TryGetValue(local, out var index))
        {
            index = _indices.Count;
            _indices.Add(local, index);
        }

        return index;
    }

    /// <summary>Adds the event list of a new node.</summary>
    public void AddNode() => _events.Add(ImmutableArray.CreateBuilder<LivenessEvent>());

    /// <summary>Starts the statement events of a new body level.</summary>
    public void PushStatement() => _statements.Add(ImmutableArray.CreateBuilder<(LivenessEvent, SyntaxNode)>());

    /// <summary>Ends the statement events of the innermost body level.</summary>
    public void PopStatement() => _statements.RemoveAt(_statements.Count - 1);

    /// <summary>Moves the statement events to <paramref name="node"/>.</summary>
    public void Flush(int node)
    {
        var statement = Statement;
        foreach (var (flowEvent, _) in statement)
        {
            _events[node].Add(flowEvent);
        }

        statement.Clear();
    }

    /// <summary>
    /// Adds an event: to the statement while it is outside every opaque body and has a syntax, else to
    /// <paramref name="node"/>.
    /// </summary>
    public void Add(LivenessEvent flowEvent, SyntaxNode? syntax, int node)
    {
        if (frames.Count == 0 && syntax != null)
        {
            Statement.Add((flowEvent, syntax));
            return;
        }

        _events[node].Add(flowEvent);
    }

    /// <summary>A read of a local or tracked parameter declared outside every opaque body the walk is inside.</summary>
    public void AddRead(ISymbol local, SyntaxNode? syntax, int node)
    {
        if (frames.IsDeclaredInFrame(local))
        {
            return;
        }

        var index = Index(local);
        Add(LivenessEvent.Read(index), syntax, node);
        frames.AddCapture(index);
    }

    /// <summary>A local assigned; inside an opaque body, or when it does not replace the value, a read.</summary>
    public void AddAssigned(ISymbol local, SyntaxNode? syntax, int node, bool replaces)
    {
        if (frames.Count > 0 || !replaces)
        {
            AddRead(local, syntax, node);
            return;
        }

        Add(LivenessEvent.Write(Index(local)), syntax, node);
    }

    /// <summary>A tracked parameter assigned as a whole: written and changed.</summary>
    public void AddParameterAssigned(int index, SyntaxNode? syntax, int node)
    {
        if (frames.Count > 0)
        {
            _events[node].Add(LivenessEvent.Read(index));
            return;
        }

        Add(LivenessEvent.Write(index), syntax, node);
        AddChange(index, syntax, node);
    }

    /// <summary>A change of a tracked parameter.</summary>
    public void AddChange(int index, SyntaxNode? syntax, int node)
    {
        Add(LivenessEvent.Change(index), syntax, node);
        HasChanges = true;
    }

    /// <summary>Pins the captures of <paramref name="frame"/> at <paramref name="node"/>.</summary>
    public void AddPins(LivenessBodyFrame frame, int node)
    {
        foreach (var captured in frame.Captures)
        {
            _events[node].Add(LivenessEvent.Pin(captured));
            HasPins = true;
        }
    }
}
