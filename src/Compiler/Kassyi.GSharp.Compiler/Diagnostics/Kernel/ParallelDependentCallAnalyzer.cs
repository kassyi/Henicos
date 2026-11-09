using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Roslyn analyzer that infers the execution context (parallel vs single-thread) of method calls
/// and reports GSH0035 if parallel-dependent logic is invoked from a single-thread context.
/// </summary>
/// <remarks>
/// A single-thread context is the body of <c>Gpu.SingleThread</c> and an <c>if</c> whose condition compares a thread or
/// block index with 0. Every method that an operation there calls is checked, whatever the syntax that calls it
/// (<see cref="OperationCalls"/>).
/// </remarks>
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

        if (context.SemanticModel.GetOperation(methodDeclaration, context.CancellationToken) is not { } body)
        {
            return;
        }

        Walk(context, inferenceCache, body, singleThread: false, ImmutableArray.CreateBuilder<IMethodSymbol>());
    }

    private static void Walk(SyntaxNodeAnalysisContext context, ConcurrentDictionary<IMethodSymbol, bool> inferenceCache,
        IOperation operation, bool singleThread, ImmutableArray<IMethodSymbol>.Builder called)
    {
        if (singleThread)
        {
            called.Clear();
            OperationCalls.Add(operation, TypeArgumentMap.None, called);
            foreach (var method in called)
            {
                if (ContextInferenceEngine.IsParallelDependent(method, inferenceCache, context.Compilation))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.ParallelDependentCallFromSingleThreadContext,
                        operation.Syntax,
                        method.Name);
                }
            }
        }

        var inner = singleThread || StartsSingleThread(operation, context.SemanticModel);
        foreach (var child in operation.ChildOperations)
        {
            Walk(context, inferenceCache, child, inner, called);
        }
    }

    // Gpu.SingleThread(...) (its body runs on one thread; the body of SyncGrid runs on every thread with a grid sync
    // between statements, so it is not a single-thread context), or if (ThreadIndex.X == 0) ...
    private static bool StartsSingleThread(IOperation operation, SemanticModel model) => operation switch
    {
        IInvocationOperation invocation =>
            GpuMacroClassifier.Classify(invocation.TargetMethod).Kind is GpuMacroKind.SingleThread,
        IConditionalOperation { Syntax: IfStatementSyntax ifStatement } =>
            IsSingleThreadDivergentCondition(ifStatement.Condition, model),
        _ => false
    };

    private static bool IsSingleThreadDivergentCondition(ExpressionSyntax condition, SemanticModel model)
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
            .Any(model, static (node, m) =>
                node is MemberAccessExpressionSyntax &&
                m.GetSymbol(node) is IPropertySymbol { ContainingType: { Name: "ThreadIndex" or "BlockIndex" } type } &&
                type.ContainingNamespace.IsCoreNamespace());

        static bool isZeroLiteral(ExpressionSyntax expr) =>
            expr is LiteralExpressionSyntax { Token.ValueText: "0" };
    }
}
