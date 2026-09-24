using Kassyi.GSharp.Compiler.Diagnostics.Rules;

namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>Roslyn analyzer that validates C# syntax rules inside GPU kernel and device methods.</summary>
/// <remarks><para>Implements: <b>GSH-00, GSH-01, GSH-02, GSH-03, GSH-04, GSH-05, GSH-06, GSH-07, GSH-08, GSH-09, GSH-0007, GSH-0008, GSH-0009, GSH-0012</b></para></remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GpuKernelAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.ForeachNotSupported,
        GSharpDiagnostics.LinqNotSupported,
        GSharpDiagnostics.ImplicitVarNeedsSuffix,
        GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral,
        GSharpDiagnostics.MacroArgumentMustBeConstant,
        GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant,
        GSharpDiagnostics.UnrollHintMustPrecedeLoop,
        GSharpDiagnostics.UnrolledLoopMustBeStatic,
        GSharpDiagnostics.UnrolledLoopMayBeIgnored,
        GSharpDiagnostics.ManualEnumeratorNotSupported);

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
        var isGpuKernel = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) ||
                          methodSymbol.HasAttribute(KnownAttributes.GpuDeviceFunction);

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
