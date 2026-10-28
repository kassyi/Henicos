namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a constant width of a warp shuffle that is not 1, 2, 4, 8, 16 or 32 (GSH0095), which CUDA leaves undefined
/// (#666). A width known only at run time is checked by the CPU paths when the shuffle runs.
/// </summary>
internal sealed class WarpShuffleWidthRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly WarpShuffleWidthRule Instance = new();

    private const string WidthParameter = "width";

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.WarpShuffleWidthNotPowerOfTwo);

    private WarpShuffleWidthRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax invocation, ref ValidationContext context)
    {
        // The shuffles of Kassyi.GSharp.Core.Warp, recognized by their symbol: the analyzer sees the user's tree, which has
        // none of the lowering annotations that BarrierHelper reads
        if (context.SemanticModel.GetSymbol(invocation) is not IMethodSymbol method ||
            SyncIntrinsicClassifier.Classify(method).Shuffle == WarpShuffleKind.None)
        {
            return;
        }

        var arguments = invocation.ArgumentList.Arguments;
        for (var i = 0; i < arguments.Count; i++)
        {
            if (method.FindParameter(arguments, i) is not { Name: WidthParameter } ||
                context.SemanticModel.GetSafeConstantValue(arguments[i].Expression) is not { HasValue: true, Value: int width } ||
                IsSupportedWidth(width))
            {
                continue;
            }

            context.ReportDiagnostic(
                GSharpDiagnostics.WarpShuffleWidthNotPowerOfTwo,
                arguments[i].Expression,
                method.Name,
                width.ToString(CultureInfo.InvariantCulture)
            );
        }
    }

    private static bool IsSupportedWidth(int width) => width is 1 or 2 or 4 or 8 or 16 or 32;
}
