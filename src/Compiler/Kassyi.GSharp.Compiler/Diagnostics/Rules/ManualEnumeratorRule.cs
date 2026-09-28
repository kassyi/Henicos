namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

internal sealed class ManualEnumeratorRule
{
    public static readonly ManualEnumeratorRule Instance = new();

    private ManualEnumeratorRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol { Name: "GetEnumerator" } symbol)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GSharpDiagnostics.ManualEnumeratorNotSupported,
                node.GetLocation()));
        }
    }
}
