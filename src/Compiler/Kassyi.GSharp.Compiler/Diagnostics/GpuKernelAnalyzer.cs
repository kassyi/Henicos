using Microsoft.CodeAnalysis.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;

namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>Roslyn analyzer that validates C# syntax rules inside GPU kernel and device methods.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GpuKernelAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.ForeachNotSupported,
        GSharpDiagnostics.LinqNotSupported,
        GSharpDiagnostics.ImplicitVarNeedsSuffix,
        GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral,
        GSharpDiagnostics.MacroArgumentMustBeConstant);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
        {
            return;
        }

        // Check if the method is a GPU kernel or device method
        var isGpuKernel = methodSymbol.GetAttributes().Any(a =>
            KnownAttributes.IsGpuKernel(a.AttributeClass?.Name) ||
            KnownAttributes.IsGpuDeviceFunction(a.AttributeClass?.Name));

        if (!isGpuKernel)
        {
            return;
        }

        // Single-pass traversal of the AST to minimize time complexity and zero allocations
        var validationContext = new ValidationContext(context.SemanticModel, methodSymbol, context.ReportDiagnostic);
        using var walker = GpuKernelValidationWalker.Rent(validationContext);

        walker.Visit(methodDeclaration);
    }
}
