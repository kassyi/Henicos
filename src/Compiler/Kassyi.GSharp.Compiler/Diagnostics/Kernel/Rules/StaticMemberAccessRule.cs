namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

internal sealed class StaticMemberAccessRule : ISyntaxValidationRule<MemberAccessExpressionSyntax>, ISyntaxValidationRule<IdentifierNameSyntax>
{
    public static readonly StaticMemberAccessRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.StaticMemberAccessNotSupported);

    private StaticMemberAccessRule()
    {
    }

    public void Analyze(MemberAccessExpressionSyntax node, ref ValidationContext context)
    {
        var symbol = context.SemanticModel.GetSymbol(node);
        CheckSymbol(symbol, node.GetLocation(), node, ref context);
    }

    public void Analyze(IdentifierNameSyntax node, ref ValidationContext context)
    {
        if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbol(node);
        CheckSymbol(symbol, node.GetLocation(), node, ref context);
    }

    private static void CheckSymbol(ISymbol? symbol, Location location, SyntaxNode node, ref ValidationContext context)
    {
        if (symbol == null ||
            !GpuCodeConstraints.IsHostStaticMember(symbol, context.Dialect) ||
            IsInsideNameof(node, context.SemanticModel))
        {
            return;
        }

        var containingTypeName = symbol.ContainingType?.Name ?? string.Empty;
        context.ReportDiagnostic(GSharpDiagnostics.StaticMemberAccessNotSupported, location, containingTypeName, symbol.Name);
    }

    private static bool IsInsideNameof(SyntaxNode node, SemanticModel semanticModel)
    {
        var curr = node.Parent;
        while (curr != null)
        {
            if (curr is InvocationExpressionSyntax inv)
            {
                if (inv.Expression is IdentifierNameSyntax id &&
                    id.Identifier.ValueText == "nameof" &&
                    semanticModel.GetSymbol(inv) == null)
                {
                    return true;
                }
            }

            curr = curr.Parent;
        }

        return false;
    }
}
