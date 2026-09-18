using Microsoft.CodeAnalysis.Diagnostics;

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
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        bool isIntrinsic = methodSymbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "CudaIntrinsicAttribute" || a.AttributeClass?.Name == "CudaIntrinsic");
        if (!isIntrinsic)
        {
            return;
        }

        var parentMethod = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (parentMethod != null)
        {
            var parentMethodSymbol = context.SemanticModel.GetDeclaredSymbol(parentMethod);
            var isGpuKernelOrDevice = parentMethodSymbol?.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "GpuKernelAttribute" ||
                a.AttributeClass?.Name == "GpuKernel" ||
                a.AttributeClass?.Name == "GpuDeviceFunctionAttribute" ||
                a.AttributeClass?.Name == "GpuDeviceFunction") == true;
            if (isGpuKernelOrDevice)
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.HostIntrinsicCallError, invocation.GetLocation(),
            methodSymbol.Name));
    }
}
