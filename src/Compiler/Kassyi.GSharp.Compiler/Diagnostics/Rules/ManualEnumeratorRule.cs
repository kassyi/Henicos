namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

internal sealed class ManualEnumeratorRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly ManualEnumeratorRule Instance = new();
    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.ManualEnumeratorNotSupported);

    private ManualEnumeratorRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is IMethodSymbol { Name: "GetEnumerator" })
        {
            context.Report(GSharpDiagnostics.ManualEnumeratorNotSupported, node);
        }
    }
}
