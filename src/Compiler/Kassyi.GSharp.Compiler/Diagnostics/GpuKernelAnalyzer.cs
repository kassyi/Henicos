
namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>Roslyn analyzer that validates C# syntax rules inside GPU kernel and device methods.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GpuKernelAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        DiagnosticRuleRegistry.AllSupportedDiagnostics.Add(GSharpDiagnostics.KernelClassMustBePartial);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDeclaration);
        if (methodSymbol == null)
        {
            return;
        }

        // Check if the method is a GPU kernel or device method
        var isGpuKernel = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) ||
                          methodSymbol.HasAttribute(KnownAttributes.GpuDeviceFunction);

        if (!isGpuKernel)
        {
            return;
        }

        // Check if the containing class is partial
        if (methodDeclaration.Parent is ClassDeclarationSyntax classDecl)
        {
            if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.KernelClassMustBePartial,
                    classDecl.Identifier.GetLocation(),
                    classDecl.Identifier.Text);
            }
        }

        // Single-pass traversal of the AST to minimize time complexity and zero allocations
        var validationContext = new ValidationContext(context.SemanticModel, methodSymbol, context.ReportDiagnostic);
        using var walker = GpuKernelValidationWalker.Rent(validationContext);

        walker.Visit(methodDeclaration);
    }
}
