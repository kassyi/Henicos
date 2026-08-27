namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>Diagnostic rule that disallows foreach loops in GPU kernels to avoid enumerator allocations.</summary>
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
