namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>
/// Ensures that prohibited APIs (like System.Math, System.Threading.Interlocked) are not used in GPU kernels.
/// </summary>
internal sealed class ProhibitedApiRule
{
    public static readonly ProhibitedApiRule Instance = new();

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

        if (symbol is
            {
                ContainingType.Name: TypeConstants.MathType or TypeConstants.MathFType
                or TypeConstants.InterlockedType or TypeConstants.BitOperationsType
            } &&
            (symbol.ContainingNamespace.IsSystemNamespace() || symbol.ContainingNamespace.IsSystemThreading() ||
             symbol.ContainingNamespace.IsSystemOrNumerics()))
        {
            context.Report(
                GSharpDiagnostics.UnsupportedSyntax,
                node,
                $"{symbol.ContainingType.Name}.{symbol.Name}");
        }
    }
}
