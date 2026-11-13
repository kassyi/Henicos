namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

using NativeApi;

/// <summary>
/// Reports a <c>[CudaImport]</c> declaration that G# cannot add a generated body to (GSH0113), and otherwise the
/// parameters that its generated launcher cannot pass to the kernel (GSH0110), from the same decisions
/// <see cref="CudaImportGenerator"/> reads.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CudaImportAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.CudaImportParameterNotSupported,
            GSharpDiagnostics.CudaImportDeclarationNotSupported
        );

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

        var declaration = method.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Select(static reference => reference.GetSyntax() as MethodDeclarationSyntax)
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        var messageName = CudaImportPlanner.GetMessageName(method);

        // A declaration that gets no generated body is reported alone: its parameters are never passed
        if (CudaImportDeclaration.GetProblem(method) is { } reason)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.CudaImportDeclarationNotSupported,
                declaration?.Identifier.GetLocation(),
                messageName,
                reason
            );
            return;
        }

        var plan = CudaImportPlanner.Plan(method, context.Compilation);
        if (plan.CanLaunch)
        {
            return;
        }

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
