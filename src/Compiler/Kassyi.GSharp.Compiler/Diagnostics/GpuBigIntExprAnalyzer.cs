using Microsoft.CodeAnalysis.Diagnostics;

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

        // Register for VariableDeclarator (e.g. var x = gpuA + gpuB;)
        context.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclarator);

        // Register for SimpleAssignmentExpression (e.g. expr1 = expr2;)
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declarator = (VariableDeclaratorSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol symbol && IsGpuBigIntExpr(symbol.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GSharpDiagnostics.GpuBigIntExprDirectAssignment,
                declarator.GetLocation()));
        }
    }

    private void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        var typeInfo = context.SemanticModel.GetTypeInfo(assignment.Left);

        // Prevent assignments where the target evaluates to GpuBigIntExpr
        if (typeInfo.Type != null && IsGpuBigIntExpr(typeInfo.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GSharpDiagnostics.GpuBigIntExprDirectAssignment,
                assignment.GetLocation()));
        }
    }

    private static bool IsGpuBigIntExpr(ITypeSymbol type)
    {
        return type.Name == "GpuBigIntExpr" &&
               type.ContainingNamespace?.ToDisplayString() is "Kassyi.GSharp.Stdlib.Math.Expressions" or "Kassyi.GSharp.GpuBigInt.Expressions";
    }
}
