using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Compiler.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GpuKernelAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.ForeachNotSupported,
        GSharpDiagnostics.LinqNotSupported,
        GSharpDiagnostics.ImplicitVarNeedsSuffix);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
        {
            return;
        }

        // Check if the method is a GPU kernel or device method
        var isGpuKernel = methodSymbol.GetAttributes().Any(a =>
            KnownAttributes.IsGpuKernel(a.AttributeClass?.Name) ||
            KnownAttributes.IsGpuDevice(a.AttributeClass?.Name));

        if (!isGpuKernel)
        {
            return;
        }

        // Single-pass traversal of the AST to minimize time complexity
        foreach (var node in methodDeclaration.DescendantNodes())
        {
            switch (node)
            {
                case ForEachStatementSyntax foreachStmt:
                    {
                        var diagnostic = Diagnostic.Create(GSharpDiagnostics.ForeachNotSupported, foreachStmt.GetLocation(),
                            methodSymbol.Name);
                        context.ReportDiagnostic(diagnostic);
                        break;
                    }
                case QueryExpressionSyntax queryExpr:
                    {
                        var diagnostic = Diagnostic.Create(GSharpDiagnostics.LinqNotSupported, queryExpr.GetLocation(),
                            methodSymbol.Name);
                        context.ReportDiagnostic(diagnostic);
                        break;
                    }
                case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Type.IsVar: true }, Initializer: { Value: LiteralExpressionSyntax literal } } variable
                    when literal.IsKind(SyntaxKind.NumericLiteralExpression):
                    {
                        var text = literal.Token.Text;
                        if (text.Contains('.') || text.Contains('e') || text.Contains('E'))
                        {
                            continue;
                        }

                        var upper = text.ToUpperInvariant();
                        if (upper.Contains("U") || upper.Contains("L"))
                        {
                            continue;
                        }

                        var diagnostic = Diagnostic.Create(GSharpDiagnostics.ImplicitVarNeedsSuffix, variable.GetLocation(),
                            variable.Identifier.Text);
                        context.ReportDiagnostic(diagnostic);
                        break;
                    }
            }
        }
    }
}
