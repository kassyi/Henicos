
namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>Diagnostic rule that disallows foreach loops in GPU kernels to avoid enumerator allocations.</summary>
internal sealed class ForEachNotSupportedRule : ISyntaxValidationRule<ForEachStatementSyntax>
{
    public static readonly ForEachNotSupportedRule Instance = new();
    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.ForeachNotSupported);

    private ForEachNotSupportedRule()
    {
    }

    public void Analyze(ForEachStatementSyntax node, ref ValidationContext context)
    {
        var type = context.SemanticModel.GetTypeSymbol(node.Expression);
        if (type is null or IErrorTypeSymbol)
        {
            return;
        }

        // Allowed: Array, InlineArray, Span/ReadOnlySpan, List
        if (type is IArrayTypeSymbol ||
            type.HasAttribute(KnownAttributes.InlineArray) ||
            (type.Name is TypeConstants.SpanType or TypeConstants.ReadOnlySpanType &&
             type.ContainingNamespace.IsSystemNamespace()) ||
            (type.Name is TypeConstants.ListType &&
             type.ContainingNamespace is
             {
                 Name: "Generic",
                 ContainingNamespace:
                 {
                     Name: "Collections",
                     ContainingNamespace:
                     { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
                 }
             }))
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.ForeachNotSupported,
            node,
            context.MethodSymbol.Name);
    }
}
