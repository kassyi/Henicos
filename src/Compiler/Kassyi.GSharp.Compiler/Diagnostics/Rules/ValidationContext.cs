namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>Encapsulates semantic and method symbol context for AST validation rules.</summary>
internal readonly struct ValidationContext(
    SemanticModel semanticModel,
    IMethodSymbol methodSymbol,
    Action<Diagnostic> reportDiagnostic)
{
    public readonly SemanticModel SemanticModel = semanticModel;
    public readonly IMethodSymbol MethodSymbol = methodSymbol;

    public void ReportDiagnostic(Diagnostic diagnostic) => reportDiagnostic(diagnostic);

    public void Report(DiagnosticDescriptor descriptor, Location? location, params object?[]? messageArgs) =>
        reportDiagnostic(Diagnostic.Create(descriptor, location, messageArgs));

    public void Report(DiagnosticDescriptor descriptor, SyntaxNode node, params object?[]? messageArgs) =>
        reportDiagnostic(Diagnostic.Create(descriptor, node.GetLocation(), messageArgs));
}
