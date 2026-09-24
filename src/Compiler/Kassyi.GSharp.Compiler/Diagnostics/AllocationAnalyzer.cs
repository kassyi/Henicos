namespace Kassyi.GSharp.Compiler.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AllocationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.UnguardedMultiAllocation,
            GSharpDiagnostics.StaticDisposableAllocation,
            GSharpDiagnostics.UnsafeFieldOverwrite,
            GSharpDiagnostics.ManualUnmanagedAllocation
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (!field.IsStatic || !DisposableAnalyzerHelper.IsDisposable(field.Type))
        {
            return;
        }

        var syntax = field.DeclaringSyntaxReferences.Length > 0
            ? field.DeclaringSyntaxReferences[0].GetSyntax() as VariableDeclaratorSyntax
            : null;
        if (syntax?.Initializer?.Value is ObjectCreationExpressionSyntax)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.StaticDisposableAllocation,
                syntax.GetLocation()));
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
                     node is ObjectCreationExpressionSyntax oce &&
                     DisposableAnalyzerHelper.IsDisposable(context.SemanticModel.GetTypeInfo(oce).Type))
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
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.UnguardedMultiAllocation,
                ctor.Identifier.GetLocation()));
        }
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Right is not ObjectCreationExpressionSyntax oce)
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(oce).Type;
        if (!DisposableAnalyzerHelper.IsDisposable(type))
        {
            return;
        }

        var leftSymbol = context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol;
        if (leftSymbol is not IFieldSymbol fieldSymbol)
        {
            return;
        }

        MethodDeclarationSyntax? enclosingMethod = null;
        for (var parent = assignment.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is not MethodDeclarationSyntax m)
            {
                continue;
            }

            enclosingMethod = m;
            break;
        }

        if (enclosingMethod == null)
        {
            return;
        }

        var priorDispose = false;
        var assignmentSpanStart = assignment.SpanStart;
        foreach (var node in enclosingMethod.DescendantNodes(n => n.SpanStart < assignmentSpanStart))
        {
            if (node is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Dispose" } ma
                } inv ||
                inv.SpanStart >= assignmentSpanStart ||
                ((ma.Expression is not IdentifierNameSyntax id || id.Identifier.Text != fieldSymbol.Name) &&
                 (ma.Expression is not MemberAccessExpressionSyntax
                 {
                     Expression: ThisExpressionSyntax
                 } fieldMa ||
                  fieldMa.Name.Identifier.Text != fieldSymbol.Name)))
            {
                continue;
            }

            priorDispose = true;
            break;
        }

        if (!priorDispose)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.UnsafeFieldOverwrite,
                assignment.GetLocation()));
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is IMethodSymbol
            {
                Name: "Malloc",
                ContainingType: { Name: "Unsafe", ContainingType.Name: "Gpu" }
            })
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.ManualUnmanagedAllocation,
                invocation.GetLocation()));
        }
    }
}
