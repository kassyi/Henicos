using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

internal readonly struct ValidationContext
{
    public readonly SemanticModel SemanticModel;
    public readonly IMethodSymbol MethodSymbol;
    private readonly Action<Diagnostic> _reportDiagnostic;

    public ValidationContext(SemanticModel semanticModel, IMethodSymbol methodSymbol, Action<Diagnostic> reportDiagnostic)
    {
        SemanticModel = semanticModel;
        MethodSymbol = methodSymbol;
        _reportDiagnostic = reportDiagnostic;
    }

    public void ReportDiagnostic(Diagnostic diagnostic)
    {
        _reportDiagnostic(diagnostic);
    }
}
