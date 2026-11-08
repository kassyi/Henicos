namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The state of a <see cref="SplicedFlowGraph"/> while <see cref="SplicedFlowBuilder"/> builds it: the nodes made so
/// far, the recorders that hear of each one, and where the walk is.
/// </summary>
internal sealed class SplicedFlowContext
{
    private readonly ImmutableArray<SplicedFlowNode>.Builder _nodes = ImmutableArray.CreateBuilder<SplicedFlowNode>();

    /// <summary>Starts the graph with its start node, node 0.</summary>
    public SplicedFlowContext(SemanticModel model, ImmutableArray<IFlowRecorder> recorders)
    {
        Model = model;
        Recorders = recorders;
        Current = NewNode();
    }

    /// <summary>The model of the body.</summary>
    public SemanticModel Model { get; }

    /// <summary>The recorders, one per analysis, in the order they hear of each operation.</summary>
    public ImmutableArray<IFlowRecorder> Recorders { get; }

    /// <summary>The local functions being spliced on the current path; a recursive call adds nothing.</summary>
    public HashSet<IMethodSymbol> LocalFunctionsInProgress { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>The node that receives the next operations.</summary>
    public SplicedFlowNode Current { get; set; }

    /// <summary>The gaps of the <c>Gpu.SyncGrid</c> body being built; null outside one.</summary>
    public SyncGridGaps? Gaps { get; set; }

    /// <summary>The element range of the last top-level operation of the block being built.</summary>
    public SyncGridElementRange? LastRange { get; set; }

    /// <summary>The element range of the top-level operation being walked.</summary>
    public SyncGridElementRange? TopRange { get; set; }

    /// <summary>The nodes made so far.</summary>
    public ImmutableArray<SplicedFlowNode> ToNodes() => _nodes.ToImmutable();

    /// <summary>A new node, which every recorder hears of.</summary>
    public SplicedFlowNode NewNode()
    {
        var node = new SplicedFlowNode(_nodes.Count);
        _nodes.Add(node);
        foreach (var recorder in Recorders)
        {
            recorder.OnNode(node.Index);
        }

        return node;
    }

    /// <summary>Lets every recorder record at <paramref name="node"/>.</summary>
    public void Record(Action<IFlowRecorder, int> record, int node)
    {
        foreach (var recorder in Recorders)
        {
            record(recorder, node);
        }
    }

    /// <summary>An edge from <paramref name="from"/> to <paramref name="to"/>, once.</summary>
    public static void Connect(SplicedFlowNode from, SplicedFlowNode to)
    {
        if (!from.Successors.Contains(to.Index))
        {
            from.Successors.Add(to.Index);
        }
    }
}
