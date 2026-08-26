namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

internal sealed class ForEachNotSupportedRule
{
    public static readonly ForEachNotSupportedRule Instance = new();

    private ForEachNotSupportedRule()
    {
    }

    public void Analyze(ForEachStatementSyntax node, ref ValidationContext context)
    {
        var diagnostic = Diagnostic.Create(
            GSharpDiagnostics.ForeachNotSupported,
            node.GetLocation(),
            context.MethodSymbol.Name);

        context.ReportDiagnostic(diagnostic);
    }
}
