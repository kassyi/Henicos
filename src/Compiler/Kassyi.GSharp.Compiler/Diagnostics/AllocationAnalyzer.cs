
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

        field.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var syntax);
        if (syntax?.Initializer?.Value is BaseObjectCreationExpressionSyntax)
        {
            context.ReportDiagnostic(GSharpDiagnostics.StaticDisposableAllocation,
                syntax.GetLocation());
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
                     node is BaseObjectCreationExpressionSyntax oce &&
                     DisposableAnalyzerHelper.IsDisposable(context.SemanticModel.GetTypeSymbol(oce)))
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
            context.ReportDiagnostic(GSharpDiagnostics.UnguardedMultiAllocation,
                ctor.Identifier.GetLocation());
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbol(invocation) is IMethodSymbol
            {
                Name: "Malloc",
                ContainingType: { Name: "Unsafe", ContainingType.Name: TypeConstants.GpuType }
            })
        {
            context.ReportDiagnostic(GSharpDiagnostics.ManualUnmanagedAllocation,
                invocation.GetLocation());
        }
    }
}
