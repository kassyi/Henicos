
namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>Roslyn analyzer that prohibits direct variable declarations or assignments of GpuBigIntExpr without .Value.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GpuBigIntExprAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.GpuBigIntExprDirectAssignment);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var type1 = compilationContext.Compilation.GetTypeByMetadataName(
                "Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions.GpuBigIntExpr");
            var type2 = compilationContext.Compilation.GetTypeByMetadataName(
                "Kassyi.GSharp.Stdlib.GpuMath.Expressions.GpuBigIntExpr");
            var type3 = compilationContext.Compilation.GetTypeByMetadataName(
                "Kassyi.GSharp.GpuBigInt.Expressions.GpuBigIntExpr");

            if (type1 is null && type2 is null && type3 is null)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(ctx => AnalyzeVariableDeclaration(ctx, type1, type2, type3),
                SyntaxKind.VariableDeclarator);
            compilationContext.RegisterSyntaxNodeAction(ctx => AnalyzeAssignment(ctx, type1, type2, type3),
                SyntaxKind.SimpleAssignmentExpression);
        });
    }

    private void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context, INamedTypeSymbol? t1,
        INamedTypeSymbol? t2, INamedTypeSymbol? t3)
    {
        var declarator = (VariableDeclaratorSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol<ILocalSymbol>(declarator) is { } symbol &&
            IsGpuBigIntExpr(symbol.Type, t1, t2, t3))
        {
            context.Report(GSharpDiagnostics.GpuBigIntExprDirectAssignment, declarator);
        }
    }

    private void AnalyzeAssignment(SyntaxNodeAnalysisContext context, INamedTypeSymbol? t1, INamedTypeSymbol? t2,
        INamedTypeSymbol? t3)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        var targetType = context.SemanticModel.GetTypeSymbol(assignment.Left);

        // Prevent assignments where the target evaluates to GpuBigIntExpr
        if (targetType != null && IsGpuBigIntExpr(targetType, t1, t2, t3))
        {
            context.Report(GSharpDiagnostics.GpuBigIntExprDirectAssignment, assignment);
        }
    }

    private static bool IsGpuBigIntExpr(ITypeSymbol type, INamedTypeSymbol? t1, INamedTypeSymbol? t2,
        INamedTypeSymbol? t3)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var originalDef = namedType.OriginalDefinition;
        return originalDef.IsEqualTo(t1) ||
               originalDef.IsEqualTo(t2) ||
               originalDef.IsEqualTo(t3);
    }
}
