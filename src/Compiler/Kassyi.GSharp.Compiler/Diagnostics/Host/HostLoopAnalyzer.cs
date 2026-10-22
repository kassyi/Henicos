
namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class HostLoopAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.AutoAllocateReturnInLoopWarning);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        IdentifierNameSyntax? identifier = null;

        if (invocation.Expression is IdentifierNameSyntax id)
        {
            identifier = id;
        }
        else if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            identifier = memberAccess.Name as IdentifierNameSyntax;
        }

        if (identifier == null || !identifier.Identifier.Text.StartsWith("Launch"))
        {
            return;
        }

        if (context.SemanticModel.GetSymbol(invocation) is not IMethodSymbol methodSymbol)
        {
            return;
        }

        if (methodSymbol.ReturnType.SpecialType == SpecialType.System_Void ||
            methodSymbol.ReturnType is not IArrayTypeSymbol)
        {
            return;
        }

        var isInsideLoop = false;
        var parent = invocation.Parent;
        while (parent != null)
        {
            if (parent is ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax
                or WhileStatementSyntax or DoStatementSyntax)
            {
                isInsideLoop = true;
                break;
            }

            if (parent is MethodDeclarationSyntax || parent is LocalFunctionStatementSyntax ||
                parent is AnonymousFunctionExpressionSyntax)
            {
                break;
            }

            parent = parent.Parent;
        }

        if (isInsideLoop)
        {
            context.ReportDiagnostic(GSharpDiagnostics.AutoAllocateReturnInLoopWarning, invocation, identifier.Identifier.Text);
        }
    }
}
