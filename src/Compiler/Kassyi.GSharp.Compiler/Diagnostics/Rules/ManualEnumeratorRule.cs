namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

internal sealed class ManualEnumeratorRule
{
    public static readonly ManualEnumeratorRule Instance = new();

    private ManualEnumeratorRule() { }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
        
        if (symbol != null && symbol.Name == "GetEnumerator")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GSharpDiagnostics.ManualEnumeratorNotSupported,
                node.GetLocation()));
        }
    }
}
