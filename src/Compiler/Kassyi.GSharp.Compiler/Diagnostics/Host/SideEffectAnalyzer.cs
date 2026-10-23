namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

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
            var getter = property.AccessorList.Accessors
                .AsStackEnumerable()
                .FirstOrDefault(static a => a.IsKind(SyntaxKind.GetAccessorDeclaration));

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
        DiagnosticDescriptor descriptor) =>
        ReportDisposableCreations(context, expression.DescendantNodesAndSelf(), descriptor);

    private static void CheckForAllocationInBlock(SyntaxNodeAnalysisContext context, BlockSyntax block,
        DiagnosticDescriptor descriptor) =>
        ReportDisposableCreations(context, block.DescendantNodes(), descriptor);

    private static void ReportDisposableCreations(
        SyntaxNodeAnalysisContext context,
        IEnumerable<SyntaxNode> nodes,
        DiagnosticDescriptor descriptor) =>
        nodes
            .AsStackEnumerable()
            .DoIf(
                (Context: context, Descriptor: descriptor),
                static (node, s) =>
                    node is BaseObjectCreationExpressionSyntax creation &&
                    DisposableAnalyzerHelper.IsDisposable(s.Context.SemanticModel.GetTypeSymbol(creation)),
                static (node, s) => s.Context.ReportDiagnostic(s.Descriptor, node)
            );
}
