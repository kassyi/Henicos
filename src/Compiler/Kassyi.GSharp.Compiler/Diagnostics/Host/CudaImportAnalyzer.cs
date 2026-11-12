namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

using NativeApi;

/// <summary>
/// Reports the parameters of a <c>[CudaImport]</c> method that its generated launcher cannot pass to the kernel
/// (GSH0110), from the same plan <see cref="CudaImportGenerator"/> writes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CudaImportAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.CudaImportParameterNotSupported);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        // The implementation part of a partial method carries the same attributes; its declaration part is reported
        if (method.PartialDefinitionPart is not null || !method.HasAttribute(KnownAttributes.CudaImport))
        {
            return;
        }

        var plan = CudaImportPlanner.Plan(method, context.Compilation);
        if (plan.CanLaunch)
        {
            return;
        }

        var declaration = method.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Select(static reference => reference.GetSyntax() as MethodDeclarationSyntax)
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        var messageName = CudaImportPlanner.GetMessageName(method);
        foreach (var problem in plan.Problems)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.CudaImportParameterNotSupported,
                GetParameterLocation(declaration, problem.ParameterOrdinal),
                problem.ParameterName,
                messageName,
                problem.TypeName,
                problem.Reason
            );
        }
    }

    // From the first modifier (or the type) to the name, without the attributes and the default value
    private static Location? GetParameterLocation(MethodDeclarationSyntax? declaration, int ordinal)
    {
        if (declaration is null || ordinal >= declaration.ParameterList.Parameters.Count)
        {
            return null;
        }

        var parameter = declaration.ParameterList.Parameters[ordinal];
        var start = parameter.Modifiers.Count > 0
            ? parameter.Modifiers[0].SpanStart
            : parameter.Type?.SpanStart ?? parameter.Identifier.SpanStart;
        return Location.Create(parameter.SyntaxTree, TextSpan.FromBounds(start, parameter.Identifier.Span.End));
    }
}
