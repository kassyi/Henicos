using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The spliced flow graph of a kernel or device function body (<see cref="SplicedFlowBuilder"/>), built once on first
/// use with what the liveness (<see cref="LivenessRecorder"/>) and the uniformity analysis
/// (<see cref="UniformityRecorder"/>) record on it, and shared by the validators of the method (GSH0037,
/// GSH0039 / GSH0041 / GSH0072, GSH0077).
/// </summary>
/// <param name="semanticModel">The semantic model of the tree that declares <paramref name="method"/>.</param>
/// <param name="method">The kernel or device function.</param>
internal sealed class MethodFlowGraph(SemanticModel semanticModel, MethodDeclarationSyntax method)
{
    private bool _built;
    private SplicedFlowGraph? _graph;
    private LivenessRecorder? _liveness;
    private UniformityRecorder? _uniformityRecorder;
    private bool _uniformityCreated;
    private UniformityAnalysis? _uniformity;

    /// <summary>
    /// The spliced graph of the body and the liveness events on it; none while the body does not bind (being edited), so
    /// nothing is reported then.
    /// </summary>
    public bool TryGetLiveness([NotNullWhen(true)] out SplicedFlowGraph? graph,
        [NotNullWhen(true)] out LivenessRecorder? liveness)
    {
        Build();
        graph = _graph;
        liveness = _liveness;
        return graph != null && liveness != null;
    }

    /// <summary>The uniformity analysis of the body, with the parameters of the method uniform.</summary>
    public bool TryGetUniformity([NotNullWhen(true)] out UniformityAnalysis? uniformity)
    {
        if (!_uniformityCreated)
        {
            _uniformityCreated = true;
            Build();
            if (_graph != null && _uniformityRecorder != null &&
                semanticModel.GetDeclaredSymbol<IMethodSymbol>(method) is { } symbol)
            {
                _uniformity = UniformityAnalysis.Analyze(_graph, _uniformityRecorder, semanticModel, symbol);
            }
        }

        uniformity = _uniformity;
        return uniformity != null;
    }

    /// <summary>
    /// The Roslyn graph of the body of <paramref name="method"/>; none when the method has no body or the body does not
    /// bind (an invalid operation).
    /// </summary>
    public static bool TryCreate(SemanticModel semanticModel, MethodDeclarationSyntax method,
        [NotNullWhen(true)] out ControlFlowGraph? graph)
    {
        graph = null;
        if (semanticModel.GetOperation(method) is not IMethodBodyOperation body)
        {
            return false;
        }

        foreach (var operation in body.Descendants())
        {
            if (operation is IInvalidOperation)
            {
                return false;
            }
        }

        graph = ControlFlowGraph.Create(body);
        return true;
    }

    // The spliced graph, once, with both recorders
    private void Build()
    {
        if (_built)
        {
            return;
        }

        _built = true;
        if (!TryCreate(semanticModel, method, out var graph) ||
            semanticModel.GetDeclaredSymbol<IMethodSymbol>(method) is not { } symbol)
        {
            return;
        }

        _liveness = new LivenessRecorder(semanticModel, method);
        _uniformityRecorder = new UniformityRecorder(symbol, default);
        _graph = SplicedFlowBuilder.Build(graph, semanticModel,
            ImmutableArray.Create<IFlowRecorder>(_liveness, _uniformityRecorder));
    }
}
