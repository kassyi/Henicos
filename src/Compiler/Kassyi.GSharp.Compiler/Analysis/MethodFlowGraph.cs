using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The control flow graph of a kernel or device function body and the <see cref="UniformityAnalysis"/> on it, built on
/// first use and shared by the validators of the method (GSH0037, GSH0039 / GSH0041 / GSH0072, GSH0077).
/// </summary>
/// <param name="semanticModel">The semantic model of the tree that declares <paramref name="method"/>.</param>
/// <param name="method">The kernel or device function.</param>
internal sealed class MethodFlowGraph(SemanticModel semanticModel, MethodDeclarationSyntax method)
{
    private bool _graphCreated;
    private ControlFlowGraph? _graph;
    private bool _uniformityCreated;
    private UniformityAnalysis? _uniformity;

    /// <summary>
    /// The graph of the body; none while the body does not bind (being edited), so nothing is reported then.
    /// </summary>
    public bool TryGetGraph([NotNullWhen(true)] out ControlFlowGraph? graph)
    {
        if (!_graphCreated)
        {
            _graphCreated = true;
            TryCreate(semanticModel, method, out _graph);
        }

        graph = _graph;
        return graph != null;
    }

    /// <summary>The uniformity analysis of the body, with the parameters of the method uniform.</summary>
    public bool TryGetUniformity([NotNullWhen(true)] out UniformityAnalysis? uniformity)
    {
        if (!_uniformityCreated)
        {
            _uniformityCreated = true;
            if (TryGetGraph(out var graph) && semanticModel.GetDeclaredSymbol<IMethodSymbol>(method) is { } symbol)
            {
                _uniformity = UniformityAnalysis.Analyze(graph, semanticModel, symbol);
            }
        }

        uniformity = _uniformity;
        return uniformity != null;
    }

    /// <summary>
    /// The graph of the body of <paramref name="method"/>; none when the method has no body or the body does not bind
    /// (an invalid operation).
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
}
