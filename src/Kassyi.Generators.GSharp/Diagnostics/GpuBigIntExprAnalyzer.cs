using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.Generators.GSharp.Diagnostics;

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
        var symbol = context.SemanticModel.GetDeclaredSymbol(declarator) as ILocalSymbol;

        if (symbol != null && IsGpuBigIntExpr(symbol.Type))
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

        // If the left side is GpuBigIntExpr, it's an invalid assignment
        // (If the left side is GpuBigInteger, CS0029 already catches it, but we can catch it too if we want.
        // Wait, if it's GpuBigInteger, CS0029 fires because there's no implicit cast.
        // But if someone tries to assign to a property OTHER than .Value... wait, property assignment is fine as long as the property handles it.
        // Actually, let's just forbid the left side evaluating to GpuBigIntExpr type to prevent `GpuBigIntExpr temp = ...`)
        if (typeInfo.Type != null && IsGpuBigIntExpr(typeInfo.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GSharpDiagnostics.GpuBigIntExprDirectAssignment, 
                assignment.GetLocation()));
        }
    }

    private bool IsGpuBigIntExpr(ITypeSymbol type)
    {
        return type.Name == "GpuBigIntExpr" && 
               type.ContainingNamespace?.ToDisplayString() == "Kassyi.GSharp.GpuBigInt.Expressions";
    }
}
