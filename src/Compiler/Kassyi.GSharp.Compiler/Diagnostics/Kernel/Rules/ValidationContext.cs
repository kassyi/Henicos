namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>Encapsulates semantic and method symbol context for AST validation rules.</summary>
/// <param name="semanticModel">The semantic model of the method.</param>
/// <param name="methodSymbol">The kernel or device function that is checked.</param>
/// <param name="reportDiagnostic">Receives the diagnostics of the rules.</param>
/// <param name="dialect">
/// The dialect of the project (<c>GSharpTargetDialect</c>), for the checks that depend on its capabilities; the C++
/// dialect when not given, as for a project that does not set the property.
/// </param>
internal readonly struct ValidationContext(
    SemanticModel semanticModel,
    IMethodSymbol methodSymbol,
    Action<Diagnostic> reportDiagnostic,
    ITranspilerDialect? dialect = null)
{
    public readonly SemanticModel SemanticModel = semanticModel;
    public readonly IMethodSymbol MethodSymbol = methodSymbol;
    public readonly ITranspilerDialect Dialect = dialect ?? TranspilerDialects.Create(TranspilerDialects.DefaultName);

    public void ReportDiagnostic(Diagnostic diagnostic) => reportDiagnostic(diagnostic);

    public void ReportDiagnostic(DiagnosticDescriptor descriptor, Location? location, params object?[]? messageArgs) =>
        reportDiagnostic.ReportDiagnostic(descriptor, location, messageArgs);

    public void ReportDiagnostic(DiagnosticDescriptor descriptor, SyntaxNode node, params object?[]? messageArgs) =>
        reportDiagnostic.ReportDiagnostic(descriptor, node, messageArgs);

}
