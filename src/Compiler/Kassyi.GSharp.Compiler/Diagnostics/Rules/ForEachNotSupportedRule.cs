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
        ITypeSymbol? type = null;
        if (node.SyntaxTree == context.SemanticModel.SyntaxTree)
        {
            try
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(node.Expression);
                type = typeInfo.Type;
            }
            catch
            {
                // Best effort if node is not bound
            }
        }

        if (type != null)
        {
            // Allowed: Array, InlineArray, Span/ReadOnlySpan, List
            if (type is IArrayTypeSymbol ||
                type.HasAttribute(KnownAttributes.InlineArray) ||
                type.Name is "Span" or "ReadOnlySpan" or "List")
            {
                return;
            }
        }

        var diagnostic = Diagnostic.Create(
            GSharpDiagnostics.ForeachNotSupported,
            node.GetLocation(),
            context.MethodSymbol.Name);

        context.ReportDiagnostic(diagnostic);
    }
}
