namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

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
