namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>Diagnostic rule that disallows LINQ query expressions in GPU kernels.</summary>
internal sealed class LinqNotSupportedRule
{
    public static readonly LinqNotSupportedRule Instance = new();

    private LinqNotSupportedRule()
    {
    }

    public void Analyze(QueryExpressionSyntax node, ref ValidationContext context)
    {
        var diagnostic = Diagnostic.Create(
            GSharpDiagnostics.LinqNotSupported,
            node.GetLocation(),
            context.MethodSymbol.Name);

        context.ReportDiagnostic(diagnostic);
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(node);
        if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
        {
            var isLinqMethod = methodSymbol.ContainingNamespace?.ToString() == "System.Linq" ||
                               methodSymbol.ContainingType?.ToString() == "System.Linq.Enumerable" ||
                               methodSymbol.ContainingType?.ToString() == "System.Linq.Queryable";

            if (isLinqMethod)
            {
                var diagnostic = Diagnostic.Create(
                    GSharpDiagnostics.LinqNotSupported,
                    node.GetLocation(),
                    context.MethodSymbol.Name);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
