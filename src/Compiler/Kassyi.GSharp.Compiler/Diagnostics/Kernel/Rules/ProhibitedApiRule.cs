namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports, in GPU code (GSH0003): the members of <c>System.Math</c> and <c>System.MathF</c> that the project's dialect
/// does not map to a native function (constants such as <c>Math.PI</c> and the mapped members are allowed), every member of
/// <c>System.Threading.Interlocked</c> and <c>System.Numerics.BitOperations</c> (#570), and the host-only StackLinq
/// operators.
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
        if (symbol == null)
        {
            return;
        }

        if (IsMathMember(symbol))
        {
            // The dialect YAML decides per overload (Math.Abs(int) is mapped, Math.Abs(long) is not)
            if (symbol is not IFieldSymbol { IsConst: true } && !context.Dialect.TryMapIntrinsic(symbol, out _))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    locationNode,
                    $"{symbol.ContainingType.Name}.{FormatMember(symbol)} (not mapped by {context.Dialect.Name})");
            }
        }
        else if (symbol is
                 {
                     ContainingType.Name: TypeConstants.InterlockedType or TypeConstants.BitOperationsType
                 } &&
                 (symbol.ContainingNamespace.IsSystemThreading() || symbol.ContainingNamespace.IsSystemOrNumerics()))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{symbol.ContainingType.Name}.{symbol.Name}");
        }
        else if (symbol is IMethodSymbol method && StackLinqHelper.IsHostOnly(method))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{method.Name} (host-only StackLinq operator)");
        }
    }

    private static bool IsMathMember(ISymbol symbol) =>
        symbol is { ContainingType.Name: TypeConstants.MathType or TypeConstants.MathFType } &&
        symbol.ContainingNamespace.IsSystemNamespace();

    // Abs(long): the overload decides whether the dialect maps it
    private static string FormatMember(ISymbol symbol) =>
        symbol is IMethodSymbol method ? IteratorTypeResolver.FormatSignature(method) : symbol.Name;
}
