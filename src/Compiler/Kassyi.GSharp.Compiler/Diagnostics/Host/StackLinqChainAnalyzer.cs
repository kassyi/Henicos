namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class StackLinqChainAnalyzer : DiagnosticAnalyzer
{
    private const int MaxChainDepth = 50;

    public static string DiagnosticId => GSharpDiagnostics.StackLinqChainTooDeep.Id;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.StackLinqChainTooDeep);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        
        // Only trigger on the outermost invocation to avoid redundant reports
        if (invocation.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax })
        {
            return;
        }

        var semanticModel = context.SemanticModel;
        var depth = 0;
        ExpressionSyntax currentExpression = invocation;

        while (currentExpression is InvocationExpressionSyntax currentInvocation)
        {
            var symbol = semanticModel.GetSymbol(currentInvocation, context.CancellationToken) as IMethodSymbol;
            
            if (symbol == null || !StackLinqHelper.IsStackLinqMethod(symbol) || symbol.ContainingType?.Name != "StackLinqExtensions")
            {
                // Not a StackLinq extension method, stop counting
                break;
            }

            depth++;

            if (currentInvocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                currentExpression = memberAccess.Expression;
            }
            else
            {
                break;
            }
        }

        if (depth > MaxChainDepth)
        {
            context.ReportDiagnostic(GSharpDiagnostics.StackLinqChainTooDeep, invocation.GetLocation(), MaxChainDepth);
        }
    }
}
