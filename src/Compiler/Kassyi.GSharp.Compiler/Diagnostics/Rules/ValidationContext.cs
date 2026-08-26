using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

internal readonly struct ValidationContext(
    SemanticModel semanticModel,
    IMethodSymbol methodSymbol,
    System.Action<Diagnostic> reportDiagnostic)
{
    public readonly SemanticModel SemanticModel = semanticModel;
    public readonly IMethodSymbol MethodSymbol = methodSymbol;

    public void ReportDiagnostic(Diagnostic diagnostic) => reportDiagnostic(diagnostic);
}
