namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Ensures that prohibited APIs (like System.Math, System.Threading.Interlocked) are not used in GPU kernels.
/// </summary>
internal sealed class ProhibitedApiRule : ISyntaxValidationRule<MemberAccessExpressionSyntax>, ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly ProhibitedApiRule Instance = new();
    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.UnsupportedSyntax);

    private ProhibitedApiRule()
    {
    }

    public void Analyze(MemberAccessExpressionSyntax node, ref ValidationContext context)
    {
        var symbol = context.SemanticModel.GetSymbol(node);

        if (symbol == null && node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
        {
            symbol = context.SemanticModel.GetSymbol(invocation);
        }

        AnalyzeSymbol(symbol, node, ref context);
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (node.Expression is MemberAccessExpressionSyntax)
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbol(node);
        AnalyzeSymbol(symbol, node, ref context);
    }

    private static void AnalyzeSymbol(ISymbol? symbol, SyntaxNode locationNode, ref ValidationContext context)
    {
        if (symbol is
            {
                ContainingType.Name: TypeConstants.MathType or TypeConstants.MathFType
                or TypeConstants.InterlockedType or TypeConstants.BitOperationsType
            } &&
            (symbol.ContainingNamespace.IsSystemNamespace() || symbol.ContainingNamespace.IsSystemThreading() ||
             symbol.ContainingNamespace.IsSystemOrNumerics()))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{symbol.ContainingType.Name}.{symbol.Name}");
        }
        else if (symbol is IMethodSymbol method && StackLinqHelper.TakesState(method))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{method.Name}(state, ...) (host-only StackLinq overload)");
        }
    }
}
