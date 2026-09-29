namespace Kassyi.GSharp.Compiler.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GpuKernelDirectInvocationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.DirectGpuKernelInvocation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        // Use OriginalDefinition to ensure we get attributes from the generic definition if applicable
        if (context.SemanticModel.GetSymbol(invocation, context.CancellationToken)?.OriginalDefinition is not IMethodSymbol methodSymbol)
        {
            return;
        }

        var isGpuKernel = methodSymbol.HasAttribute(KnownAttributes.GpuKernel);

        if (!isGpuKernel)
        {
            return;
        }

        // Check if the caller itself is a GPU kernel or device function
        var enclosingSymbol = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart, context.CancellationToken);
        while (enclosingSymbol != null)
        {
            var isEnclosingGpuFunction = enclosingSymbol.HasAttribute(KnownAttributes.GpuKernel) ||
                                         enclosingSymbol.HasAttribute(KnownAttributes.GpuDeviceFunction);

            if (isEnclosingGpuFunction)
            {
                // Device-to-device call is allowed
                return;
            }

            enclosingSymbol = enclosingSymbol.ContainingSymbol;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            GSharpDiagnostics.DirectGpuKernelInvocation,
            invocation.GetLocation(),
            methodSymbol.Name));
    }
}
