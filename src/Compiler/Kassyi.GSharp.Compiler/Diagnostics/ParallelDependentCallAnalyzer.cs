using System.Collections.Concurrent;

namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>
/// Roslyn analyzer that infers the execution context (parallel vs single-thread) of method calls 
/// and reports GSH0035 if parallel-dependent logic is invoked from a single-thread context.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ParallelDependentCallAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => 
        ImmutableArray.Create(GSharpDiagnostics.ParallelDependentCallFromSingleThreadContext);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var inferenceCache = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
            compilationContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeMethod(ctx, inferenceCache), 
                SyntaxKind.MethodDeclaration);
        });
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context, ConcurrentDictionary<IMethodSymbol, bool> inferenceCache)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
        {
            return;
        }

        // Only analyze GPU kernels or device functions
        var isGpuMethod = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) ||
                          methodSymbol.HasAttribute(KnownAttributes.GpuDeviceFunction);

        if (!isGpuMethod)
        {
            return;
        }

        var walker = new ContextWalker(context, inferenceCache);
        walker.Visit(methodDeclaration);
    }

    private class ContextWalker(
        SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<IMethodSymbol, bool> inferenceCache)
        : CSharpSyntaxWalker
    {
        private int _singleThreadDepth;

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
            bool isSingleThreadMacro = false;

            if (symbol != null && 
                (symbol.Name == TypeConstants.GpuMacros.SingleThread || symbol.Name == TypeConstants.GpuMacros.SyncGrid) && 
                symbol.ContainingType?.Name == TypeConstants.GpuType && 
                symbol.ContainingType?.ContainingNamespace?.ToString() == TypeConstants.CoreNamespace)
            {
                // For SyncGrid, it's only a single thread macro if it has arguments (a delegate)
                if (symbol.Name == TypeConstants.GpuMacros.SyncGrid && node.ArgumentList.Arguments.Count == 0)
                {
                    isSingleThreadMacro = false;
                }
                else
                {
                    isSingleThreadMacro = true;
                }
            }

            if (isSingleThreadMacro)
            {
                _singleThreadDepth++;
                base.VisitInvocationExpression(node);
                _singleThreadDepth--;
                return;
            }

            if (_singleThreadDepth > 0 && symbol != null)
            {
                CheckAndReportIfParallelDependent(node, symbol);
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            if (_singleThreadDepth > 0)
            {
                if (context.SemanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol symbol)
                {
                    CheckAndReportIfParallelDependent(node, symbol);
                }
            }
            base.VisitObjectCreationExpression(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (_singleThreadDepth > 0)
            {
                if (context.SemanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } symbol)
                {
                    CheckAndReportIfParallelDependent(node, symbol);
                }
            }
            base.VisitBinaryExpression(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (_singleThreadDepth > 0)
            {
                if (context.SemanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } symbol)
                {
                    CheckAndReportIfParallelDependent(node, symbol);
                }

                // Check property setter
                if (context.SemanticModel.GetSymbolInfo(node.Left).Symbol is IPropertySymbol { SetMethod: not null } propertySymbol)
                {
                    CheckAndReportIfParallelDependent(node, propertySymbol.SetMethod);
                }
            }
            base.VisitAssignmentExpression(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (_singleThreadDepth > 0)
            {
                // Check property getter
                if (context.SemanticModel.GetSymbolInfo(node).Symbol is IPropertySymbol { GetMethod: not null } propertySymbol)
                {
                    CheckAndReportIfParallelDependent(node, propertySymbol.GetMethod);
                }
            }
            base.VisitMemberAccessExpression(node);
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            bool isDivergentSingleThread = IsSingleThreadDivergentCondition(node.Condition);
            
            if (isDivergentSingleThread)
            {
                _singleThreadDepth++;
            }
            
            base.VisitIfStatement(node);
            
            if (isDivergentSingleThread)
            {
                _singleThreadDepth--;
            }
        }

        private bool IsSingleThreadDivergentCondition(ExpressionSyntax condition)
        {
            // Simple heuristic to detect single thread divergence (e.g., if (Gpu.ThreadIndex.X == 0))
            var text = condition.ToString();
            return (text.Contains("ThreadIndex") || text.Contains("GlobalThreadIndex") || text.Contains("BlockIndex")) && 
                   text.Contains("==") && text.Contains("0");
        }

        private void CheckAndReportIfParallelDependent(SyntaxNode node, IMethodSymbol methodSymbol)
        {
            if (ContextInferenceEngine.IsParallelDependent(methodSymbol, inferenceCache, context.Compilation))
            {
                var diagnostic = Diagnostic.Create(
                    GSharpDiagnostics.ParallelDependentCallFromSingleThreadContext,
                    node.GetLocation(),
                    methodSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
