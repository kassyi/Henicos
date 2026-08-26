using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

internal readonly struct ValidationContext
{
    public readonly SyntaxNodeAnalysisContext AnalyzerContext;
    public readonly IMethodSymbol MethodSymbol;

    public ValidationContext(SyntaxNodeAnalysisContext analyzerContext, IMethodSymbol methodSymbol)
    {
        AnalyzerContext = analyzerContext;
        MethodSymbol = methodSymbol;
    }

    public void ReportDiagnostic(Diagnostic diagnostic)
    {
        AnalyzerContext.ReportDiagnostic(diagnostic);
    }
}
