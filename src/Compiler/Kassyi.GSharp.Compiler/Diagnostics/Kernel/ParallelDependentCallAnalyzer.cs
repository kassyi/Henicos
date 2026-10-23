namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

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

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<IMethodSymbol, bool> inferenceCache)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDeclaration);
        if (methodSymbol == null)
        {
            return;
        }

        // Only analyze GPU kernels or device functions
        if (!GpuExecutionSpace.IsGpuFunction(methodSymbol))
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
            var symbol = context.SemanticModel.GetSymbol(node) as IMethodSymbol;
            // The body of SyncGrid runs on every thread with a grid sync between statements, so it is not a single-thread context
            var isSingleThreadMacro = symbol != null &&
                                      GpuMacroClassifier.Classify(symbol).Kind is GpuMacroKind.SingleThread;

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
                if (context.SemanticModel.GetSymbol(node) is IMethodSymbol symbol)
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
                if (context.SemanticModel.GetSymbol(node) is IMethodSymbol
                    {
                        MethodKind: MethodKind.UserDefinedOperator
                    } symbol)
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
                if (context.SemanticModel.GetSymbol(node) is IMethodSymbol
                    {
                        MethodKind: MethodKind.UserDefinedOperator
                    } symbol)
                {
                    CheckAndReportIfParallelDependent(node, symbol);
                }

                // Check property setter
                if (context.SemanticModel.GetSymbol(node.Left) is IPropertySymbol
                    {
                        SetMethod: not null
                    } propertySymbol)
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
                if (context.SemanticModel.GetSymbol(node) is IPropertySymbol { GetMethod: not null } propertySymbol)
                {
                    CheckAndReportIfParallelDependent(node, propertySymbol.GetMethod);
                }
            }

            base.VisitMemberAccessExpression(node);
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            var isDivergentSingleThread = IsSingleThreadDivergentCondition(node.Condition);

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
            if (condition is not BinaryExpressionSyntax binary || !binary.IsKind(SyntaxKind.EqualsExpression))
            {
                return false;
            }

            ExpressionSyntax targetExpr;
            if (isZeroLiteral(binary.Left))
            {
                targetExpr = binary.Right;
            }
            else if (isZeroLiteral(binary.Right))
            {
                targetExpr = binary.Left;
            }
            else
            {
                return false;
            }

            return targetExpr.DescendantNodesAndSelf()
                .AsStackEnumerable()
                .Any(context.SemanticModel, static (node, model) =>
                    node is MemberAccessExpressionSyntax &&
                    model.GetSymbol(node) is IPropertySymbol { ContainingType: { Name: "ThreadIndex" or "BlockIndex" } type } &&
                    type.ContainingNamespace.IsCoreNamespace());

            static bool isZeroLiteral(ExpressionSyntax expr) =>
                expr is LiteralExpressionSyntax { Token.ValueText: "0" };
        }

        private void CheckAndReportIfParallelDependent(SyntaxNode node, IMethodSymbol methodSymbol)
        {
            if (ContextInferenceEngine.IsParallelDependent(methodSymbol, inferenceCache, context.Compilation))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.ParallelDependentCallFromSingleThreadContext,
                    node,
                    methodSymbol.Name);
            }
        }
    }
}
