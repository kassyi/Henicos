namespace Kassyi.GSharp.Compiler.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AllocationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.UnguardedMultiAllocation,
            GSharpDiagnostics.StaticDisposableAllocation,
            GSharpDiagnostics.ManualUnmanagedAllocation
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (!field.IsStatic || !DisposableAnalyzerHelper.IsDisposable(field.Type))
        {
            return;
        }

        var syntax = field.DeclaringSyntaxReferences.Length > 0
            ? field.DeclaringSyntaxReferences[0].GetSyntax() as VariableDeclaratorSyntax
            : null;
        if (syntax?.Initializer?.Value is ObjectCreationExpressionSyntax)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.StaticDisposableAllocation,
                syntax.GetLocation()));
        }
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var ctor = (ConstructorDeclarationSyntax)context.Node;
        if (ctor.Body == null)
        {
            return;
        }

        var allocationCount = 0;
        var hasTry = false;

        // Single-pass scan for both allocations and try statements with early-exit
        foreach (var node in ctor.Body.DescendantNodes())
        {
            if (!hasTry && node is TryStatementSyntax)
            {
                hasTry = true;
                if (allocationCount >= 2)
                {
                    break;
                }
            }
            else if (allocationCount < 2 &&
                     node is ObjectCreationExpressionSyntax oce &&
                     DisposableAnalyzerHelper.IsDisposable(context.SemanticModel.GetTypeInfo(oce).Type))
            {
                allocationCount++;
                if (allocationCount >= 2 && hasTry)
                {
                    break;
                }
            }
        }

        if (allocationCount >= 2 && !hasTry)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.UnguardedMultiAllocation,
                ctor.Identifier.GetLocation()));
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is IMethodSymbol
            {
                Name: "Malloc",
                ContainingType: { Name: "Unsafe", ContainingType.Name: TypeConstants.GpuType }
            })
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.ManualUnmanagedAllocation,
                invocation.GetLocation()));
        }
    }
}
