namespace Kassyi.GSharp.Compiler.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class HostIntrinsicAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.HostIntrinsicCallError);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbol(invocation) is not IMethodSymbol methodSymbol)
        {
            return;
        }

        bool isIntrinsic = methodSymbol.HasAttribute(KnownAttributes.CudaIntrinsic);
        if (!isIntrinsic)
        {
            return;
        }

        var enclosingSymbol = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart);
        while (enclosingSymbol != null)
        {
            var isGpuKernelOrDevice = enclosingSymbol.HasAttribute(KnownAttributes.GpuKernel) ||
                                      enclosingSymbol.HasAttribute(KnownAttributes.GpuDeviceFunction);
            if (isGpuKernelOrDevice)
            {
                return;
            }
            enclosingSymbol = enclosingSymbol.ContainingSymbol;
        }

        context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.HostIntrinsicCallError, invocation.GetLocation(),
            methodSymbol.Name));
    }
}
