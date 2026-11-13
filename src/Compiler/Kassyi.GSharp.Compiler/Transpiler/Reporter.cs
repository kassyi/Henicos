namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Collects the constructs that the transpilation could not emit (<see cref="TranspileFailure"/>).</summary>
/// <param name="reportFailure">Receives each failure; the analyzer reports it, the generator states it in the output.</param>
/// <param name="semanticModel">The semantic model that maps a node of a lowered body to its original location.</param>
public readonly struct Reporter(Action<TranspileFailure>? reportFailure, SemanticModel semanticModel)
{
    /// <summary>
    /// Records that the output is incomplete because of <paramref name="node"/>, which the analyzer reports with
    /// <paramref name="descriptor"/>. A node of a lowered body is placed at its original location
    /// (<see cref="SemanticModelExtensions.GetReportLocation"/>).
    /// </summary>
    public void Fail(DiagnosticDescriptor descriptor, SyntaxNode node, params object?[] messageArgs) =>
        reportFailure?.Invoke(new TranspileFailure(descriptor, semanticModel.GetReportLocation(node), messageArgs));

    /// <inheritdoc cref="Fail(DiagnosticDescriptor, SyntaxNode, object?[])"/>
    public void Fail(DiagnosticDescriptor descriptor, Location location, params object?[] messageArgs) =>
        reportFailure?.Invoke(new TranspileFailure(descriptor, location, messageArgs));

    /// <summary>
    /// The same sink with another semantic model, for a body bound in another tree (a struct's computed property getter);
    /// its failures reach the same transpilation.
    /// </summary>
    public Reporter WithSemanticModel(SemanticModel model) => new(reportFailure, model);

    /// <summary>
    /// The same sink, which also calls <paramref name="onFailure"/> for each failure; with <paramref name="forward"/>
    /// false the failures reach <paramref name="onFailure"/> only (a part of a device code export that keeps its own
    /// failures, <c>StructSourcePart.Failed</c>).
    /// </summary>
    public Reporter Intercept(Action onFailure, bool forward)
    {
        var sink = reportFailure;
        return new Reporter(failure =>
        {
            onFailure();
            if (forward)
            {
                sink?.Invoke(failure);
            }
        }, semanticModel);
    }
}
