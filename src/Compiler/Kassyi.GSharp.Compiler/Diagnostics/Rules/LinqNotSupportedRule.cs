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
        context.Report(
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
                context.Report(
                    GSharpDiagnostics.LinqNotSupported,
                    node,
                    context.MethodSymbol.Name);
            }
        }
    }
}
