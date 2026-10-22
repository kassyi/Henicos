namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

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

        var isIntrinsic = methodSymbol.HasAttribute(KnownAttributes.CudaIntrinsic);
        if (!isIntrinsic)
        {
            return;
        }

        if (GpuExecutionSpace.IsWithinGpuFunction(
                context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart, context.CancellationToken)))
        {
            return;
        }

        context.ReportDiagnostic(GSharpDiagnostics.HostIntrinsicCallError, invocation, methodSymbol.Name);
    }
}
