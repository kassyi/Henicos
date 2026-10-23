namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>Roslyn analyzer that validates C# syntax rules inside GPU kernel and device methods.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GpuKernelAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        DiagnosticRuleRegistry.AllSupportedDiagnostics
            .Add(GSharpDiagnostics.KernelClassMustBePartial);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            // The checks that depend on the dialect see the one the generator emits for
            var dialect = TranspilerDialects.Create(
                TranspilerDialects.GetName(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions));
            start.RegisterSyntaxNodeAction(context => AnalyzeMethod(context, dialect), SyntaxKind.MethodDeclaration);
            start.RegisterSyntaxNodeAction(context => AnalyzeDestructor(context, dialect), SyntaxKind.DestructorDeclaration);
        });
    }

    // A finalizer marked as GPU code is reported by its rule (GpuFinalizerRule); the generator does not emit it
    private static void AnalyzeDestructor(SyntaxNodeAnalysisContext context, ITranspilerDialect dialect)
    {
        var destructor = (DestructorDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(destructor) is not { } symbol ||
            !GpuExecutionSpace.IsGpuFunction(symbol))
        {
            return;
        }

        var validationContext = new ValidationContext(context.SemanticModel, symbol, context.ReportDiagnostic, dialect);
        using var walker = GpuKernelValidationWalker.Rent(validationContext);
        walker.Visit(destructor);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context, ITranspilerDialect dialect)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDeclaration);
        if (methodSymbol == null)
        {
            return;
        }

        if (!GpuExecutionSpace.IsGpuFunction(methodSymbol))
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

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // Single-pass traversal of the AST to minimize time complexity and zero allocations
        var validationContext = new ValidationContext(context.SemanticModel, methodSymbol, diagnostics.Add, dialect);
        using (var walker = GpuKernelValidationWalker.Rent(validationContext))
        {
            walker.Visit(methodDeclaration);
        }

        TranspilerFailureCheck.AddFailures(methodDeclaration, methodSymbol, context.SemanticModel, diagnostics);

        // A rule and the transpiler, or the signature and the body, find the same construct: one diagnostic per ID and location
        var reported = new HashSet<(string Id, Location Location)>();
        diagnostics
            .AsStackEnumerable()
            .Where(reported, static (diagnostic, seen) => seen.Add((diagnostic.Id, diagnostic.Location)))
            .Do(context, static (diagnostic, analysis) => analysis.ReportDiagnostic(diagnostic));
    }
}
