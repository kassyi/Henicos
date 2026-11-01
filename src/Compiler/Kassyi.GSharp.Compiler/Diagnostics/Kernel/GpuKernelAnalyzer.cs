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
            // Exported unless the project turns it off, as the generator reads it
            var exports = !(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                                .TryGetValue("build_property.GSharpExportDeviceCode", out var export) &&
                            string.Equals(export, "false", StringComparison.OrdinalIgnoreCase));
            // One GSH0036 per type declaration, however many GPU methods it holds or encloses
            var reportedTypes = new ConcurrentDictionary<TypeDeclarationSyntax, byte>();
            start.RegisterSyntaxNodeAction(context => AnalyzeMethod(context, dialect, exports, reportedTypes),
                SyntaxKind.MethodDeclaration);
            start.RegisterSyntaxNodeAction(context => AnalyzeDestructor(context, dialect, exports),
                SyntaxKind.DestructorDeclaration);
        });
    }

    // A finalizer marked as GPU code is reported by its rule (GpuFinalizerRule); the generator does not emit it
    private static void AnalyzeDestructor(SyntaxNodeAnalysisContext context, ITranspilerDialect dialect, bool exports)
    {
        var destructor = (DestructorDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(destructor) is not { } symbol ||
            !GpuExecutionSpace.IsGpuFunction(symbol))
        {
            return;
        }

        var validationContext = new ValidationContext(context.SemanticModel, symbol, context.ReportDiagnostic, dialect,
            exports);
        using var walker = GpuKernelValidationWalker.Rent(validationContext);
        walker.Visit(destructor);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context, ITranspilerDialect dialect, bool exports,
        ConcurrentDictionary<TypeDeclarationSyntax, byte> reportedTypes)
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

        // The generator adds members to the class that holds GPU code, so that class and every type enclosing it must be
        // partial (#941). A struct that holds device functions is not reopened, so it is not reported.
        var reopened = methodDeclaration.Parent is ClassDeclarationSyntax ? methodDeclaration.Parent : null;
        for (var parent = reopened; parent is TypeDeclarationSyntax typeDecl; parent = parent.Parent)
        {
            if (!typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword) && reportedTypes.TryAdd(typeDecl, 0))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.KernelClassMustBePartial,
                    typeDecl.Identifier.GetLocation(),
                    typeDecl.Identifier.Text);
            }
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // Single-pass traversal of the AST to minimize time complexity and zero allocations
        var validationContext = new ValidationContext(context.SemanticModel, methodSymbol, diagnostics.Add, dialect,
            exports);
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
