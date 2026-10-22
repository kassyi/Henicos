namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>Diagnostic rule that disallows LINQ query expressions in GPU kernels.</summary>
internal sealed class LinqNotSupportedRule : ISyntaxValidationRule<QueryExpressionSyntax>, ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly LinqNotSupportedRule Instance = new();
    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.LinqNotSupported);

    private LinqNotSupportedRule()
    {
    }

    public void Analyze(QueryExpressionSyntax node, ref ValidationContext context)
    {
        context.ReportDiagnostic(
            GSharpDiagnostics.LinqNotSupported,
            node,
            context.MethodSymbol.Name);
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is IMethodSymbol methodSymbol)
        {
            var isLinqMethod = methodSymbol.ContainingNamespace.IsSystemLinq() ||
                               methodSymbol.ContainingType?.ContainingNamespace.IsSystemLinq() == true;

            if (isLinqMethod)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.LinqNotSupported,
                    node,
                    context.MethodSymbol.Name);
            }
        }
    }
}
