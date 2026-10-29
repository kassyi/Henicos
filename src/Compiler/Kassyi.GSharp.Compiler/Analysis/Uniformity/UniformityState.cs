namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The state that the parts of one <see cref="UniformityAnalysis"/> share: the method, its spliced graph and what was
/// recorded on it, the solution, and the parts themselves, which the facade creates.
/// </summary>
internal sealed class UniformityState
{
    public UniformityState(SemanticModel model, IMethodSymbol method, SplicedFlowGraph graph, UniformityRecorder recorder,
        HashSet<IMethodSymbol> calleesInProgress)
    {
        Model = model;
        Method = method;
        Graph = graph;
        Recorder = recorder;
        CalleesInProgress = calleesInProgress;
        Successors = graph.GetSuccessors();
        PostDominators = new PostDominatorTree(Successors, graph.Exit);
        Reader = new UniformityValueReader(this);
        Solver = new UniformitySolver(this);
        Barriers = new BarrierClassifier(this);
    }

    public SemanticModel Model { get; }

    /// <summary>The analyzed method.</summary>
    public IMethodSymbol Method { get; }

    public SplicedFlowGraph Graph { get; }

    public UniformityRecorder Recorder { get; }

    /// <summary>The device functions being analyzed on the way to this one, which a recursive call leaves out.</summary>
    public HashSet<IMethodSymbol> CalleesInProgress { get; }

    /// <summary>The successors of each node.</summary>
    public ImmutableArray<int>[] Successors { get; }

    public int NodeCount => Successors.Length;

    public PostDominatorTree PostDominators { get; }

    public UniformityValueReader Reader { get; }

    public UniformitySolver Solver { get; }

    public BarrierClassifier Barriers { get; }

    /// <summary>The variance of each value at the start of each node.</summary>
    public ThreadVariance[][] In { get; set; } = [];

    /// <summary>The variance of the branches that decide whether each node runs.</summary>
    public ThreadVariance[] Region { get; set; } = [];

    /// <summary>The variance of each node's own branch.</summary>
    public ThreadVariance[] Branch { get; set; } = [];

    /// <summary>The variance of each expression the graph evaluates, once recorded (<see cref="UniformitySolver.Observe"/>).</summary>
    public Dictionary<SyntaxNode, ThreadVariance>? Observed { get; set; }

    /// <summary>The argument masks of each barrier-reaching call, once recorded.</summary>
    public Dictionary<SyntaxNode, ArgumentMasks>? SiteMasks { get; set; }

    /// <summary>The node data that the recorder kept for the graph node <paramref name="node"/>.</summary>
    public UniformityNode NodeAt(int node) => Recorder.Nodes[node];
}
