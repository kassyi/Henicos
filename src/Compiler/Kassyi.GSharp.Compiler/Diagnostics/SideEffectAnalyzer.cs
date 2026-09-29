namespace Kassyi.GSharp.Compiler.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SideEffectAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.PropertyGetterSideEffect,
            GSharpDiagnostics.CastOperatorSideEffect
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeConversionOperator, SyntaxKind.ConversionOperatorDeclaration);
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (property.ExpressionBody != null)
        {
            CheckForAllocationInExpression(context, property.ExpressionBody.Expression,
                GSharpDiagnostics.PropertyGetterSideEffect);
        }
        else if (property.AccessorList != null)
        {
            AccessorDeclarationSyntax? getter = null;
            foreach (var a in property.AccessorList.Accessors)
            {
                if (a.Kind() != SyntaxKind.GetAccessorDeclaration)
                {
                    continue;
                }

                getter = a;
                break;
            }

            if (getter == null)
            {
                return;
            }

            if (getter.ExpressionBody != null)
            {
                CheckForAllocationInExpression(context, getter.ExpressionBody.Expression,
                    GSharpDiagnostics.PropertyGetterSideEffect);
            }
            else if (getter.Body != null)
            {
                CheckForAllocationInBlock(context, getter.Body, GSharpDiagnostics.PropertyGetterSideEffect);
            }
        }
    }

    private static void AnalyzeConversionOperator(SyntaxNodeAnalysisContext context)
    {
        var operatorDecl = (ConversionOperatorDeclarationSyntax)context.Node;
        if (operatorDecl.ExpressionBody != null)
        {
            CheckForAllocationInExpression(context, operatorDecl.ExpressionBody.Expression,
                GSharpDiagnostics.CastOperatorSideEffect);
        }
        else if (operatorDecl.Body != null)
        {
            CheckForAllocationInBlock(context, operatorDecl.Body, GSharpDiagnostics.CastOperatorSideEffect);
        }
    }

    private static void CheckForAllocationInExpression(SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression,
        DiagnosticDescriptor descriptor)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is ObjectCreationExpressionSyntax oce &&
                DisposableAnalyzerHelper.IsDisposable(context.SemanticModel.GetTypeSymbol(oce)))
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, oce.GetLocation()));
            }
        }
    }

    private static void CheckForAllocationInBlock(SyntaxNodeAnalysisContext context, BlockSyntax block,
        DiagnosticDescriptor descriptor)
    {
        foreach (var node in block.DescendantNodes())
        {
            if (node is ObjectCreationExpressionSyntax oce &&
                DisposableAnalyzerHelper.IsDisposable(context.SemanticModel.GetTypeSymbol(oce)))
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, oce.GetLocation()));
            }
        }
    }
}
