namespace Kassyi.GSharp.Compiler.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class StackLinqChainAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SLQ0001";
    private const int MaxChainDepth = 50;

    private static readonly LocalizableString Title = "StackLinq method chain is too deep";
    private static readonly LocalizableString MessageFormat = "StackLinq method chain exceeds the maximum allowed depth of {0}. Too many nested operators will cause template instantiation limits and compiler crashes. Consider breaking the query into multiple variables.";
    private static readonly LocalizableString Description = "Excessive method chaining on StackLinq types can lead to compiler resource exhaustion during C++ transpilation.";
    private const string Category = "CompilerLimits";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

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
            var symbol = semanticModel.GetSymbolInfo(currentInvocation, context.CancellationToken).Symbol as IMethodSymbol;
            
            if (symbol == null ||
                symbol.ContainingType?.Name != "StackLinqExtensions" ||
                symbol.ContainingNamespace?.Name != "Linq" ||
                symbol.ContainingNamespace?.ContainingNamespace?.Name != "ZeroAlloc" ||
                symbol.ContainingNamespace?.ContainingNamespace?.ContainingNamespace?.Name != "Kassyi")
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
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), MaxChainDepth);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
