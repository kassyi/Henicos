using System.Diagnostics.CodeAnalysis;
using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>Encapsulates semantic and method symbol context for AST validation rules.</summary>
/// <param name="semanticModel">The semantic model of the method.</param>
/// <param name="methodSymbol">The kernel or device function that is checked.</param>
/// <param name="reportDiagnostic">Receives the diagnostics of the rules.</param>
/// <param name="dialect">
/// The dialect of the project (<c>GSharpTargetDialect</c>), for the checks that depend on its capabilities; the C++
/// dialect when not given, as for a project that does not set the property.
/// </param>
/// <param name="exportsDeviceCode">Whether the project exports its device code (<c>GSharpExportDeviceCode</c>).</param>
internal readonly struct ValidationContext(
    SemanticModel semanticModel,
    IMethodSymbol methodSymbol,
    Action<Diagnostic> reportDiagnostic,
    ITranspilerDialect? dialect = null,
    bool exportsDeviceCode = true)
{
    private sealed class MethodAnalysisCache
    {
        public MethodFlowGraph? FlowGraph;
        public readonly Dictionary<ArgumentMasks, UniformityAnalysis?> Uniformity = new();
    }

    private readonly MethodAnalysisCache _analysisCache = new();

    public readonly SemanticModel SemanticModel = semanticModel;
    public readonly IMethodSymbol MethodSymbol = methodSymbol;
    public readonly ITranspilerDialect Dialect = dialect ?? TranspilerDialects.Create(TranspilerDialects.DefaultName);

    /// <summary>Whether the project exports its device code (<c>GSharpExportDeviceCode</c>, the default).</summary>
    public readonly bool ExportsDeviceCode = exportsDeviceCode;

    /// <summary>
    /// Whether the method being checked has a managed form that calls the managed state machines of the suspending
    /// functions it calls: a GPU function on the C++ dialect (the managed fallback), or a device function of a project
    /// that exports its device code (whose state machine is emitted on every dialect).
    /// </summary>
    public bool NeedsManagedStateMachines =>
        !Dialect.IsCuda || (ExportsDeviceCode && !MethodSymbol.HasAttribute(KnownAttributes.GpuKernel));

    /// <summary>
    /// The uniformity analysis of <paramref name="declaration"/>, solved once for the given argument masks.
    /// </summary>
    public bool TryGetUniformity(MethodDeclarationSyntax declaration, ArgumentMasks masks,
        [NotNullWhen(true)] out UniformityAnalysis? uniformity)
    {
        var cache = _analysisCache;
        if (cache != null && cache.Uniformity.TryGetValue(masks, out uniformity))
        {
            return uniformity != null;
        }

        var flow = cache?.FlowGraph ?? new MethodFlowGraph(SemanticModel, declaration);
        if (cache != null)
        {
            cache.FlowGraph = flow;
        }

        if (flow.TryGetUniformity(masks, out uniformity))
        {
            if (cache != null)
            {
                cache.Uniformity[masks] = uniformity;
            }

            return true;
        }

        if (cache != null)
        {
            cache.Uniformity[masks] = null;
        }

        return false;
    }

    public void ReportDiagnostic(Diagnostic diagnostic) => reportDiagnostic(diagnostic);

    public void ReportDiagnostic(DiagnosticDescriptor descriptor, Location? location, params object?[]? messageArgs) =>
        reportDiagnostic.ReportDiagnostic(descriptor, location, messageArgs);

    public void ReportDiagnostic(DiagnosticDescriptor descriptor, SyntaxNode node, params object?[]? messageArgs) =>
        reportDiagnostic.ReportDiagnostic(descriptor, node, messageArgs);

}
