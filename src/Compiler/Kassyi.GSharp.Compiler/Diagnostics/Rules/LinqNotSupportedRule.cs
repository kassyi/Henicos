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
}
