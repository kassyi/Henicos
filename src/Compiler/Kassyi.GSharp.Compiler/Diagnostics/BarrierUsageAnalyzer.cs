
namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>
/// Roslyn analyzer that enforces GPU kernel barrier synchronization and state machine constraints (GSH0037 - GSH0047, GSH0063).
/// Optimized for minimal AST passes, zero-allocation traversal, and fast-path exits.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BarrierUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.DivergentBarrierProhibited, // GSH0037
            GSharpDiagnostics.ExceptionHandlingInKernelProhibited, // GSH0038
            GSharpDiagnostics.CrossBarrierRefStructProhibited, // GSH0039
            GSharpDiagnostics.CrossBarrierGotoProhibited, // GSH0040
            GSharpDiagnostics.CrossBarrierLocalPointerProhibited, // GSH0041
            GSharpDiagnostics.ExternalBarrierCallProhibited, // GSH0042
            GSharpDiagnostics.FixedBlockBarrierProhibited, // GSH0043
            GSharpDiagnostics.KernelParameterRefStructProhibited, // GSH0044
            GSharpDiagnostics.RecursiveBarrierProhibited, // GSH0045
            GSharpDiagnostics.AsyncYieldInKernelProhibited, // GSH0046
            GSharpDiagnostics.LockStatementInKernelProhibited, // GSH0047
            GSharpDiagnostics.RecursiveDeviceFunctionBarrierProhibited // GSH0063
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Pinpoint hook for Gpu.SyncThreads() calls: O(1) detection for external methods (GSH0042)
        // Eliminates the need to traverse full ASTs of thousands of regular non-GPU methods.
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

        // Comprehensive state machine rules: only invoked for [GpuKernel] entrypoints.
        context.RegisterSyntaxNodeAction(AnalyzeKernelMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsSyncThreadsInvocation(invocation, context.SemanticModel))
        {
            return;
        }

        // Find enclosing method symbol
        var enclosingSymbol = context.ContainingSymbol;
        while (enclosingSymbol != null && enclosingSymbol.Kind != SymbolKind.Method)
        {
            enclosingSymbol = enclosingSymbol.ContainingSymbol;
        }

        if (enclosingSymbol is not IMethodSymbol enclosingMethod)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.ExternalBarrierCallProhibited,
                invocation.GetLocation());
            return;
        }

        var isGpuMethod = enclosingMethod.HasAttribute(KnownAttributes.GpuKernel) ||
                          enclosingMethod.HasAttribute(KnownAttributes.GpuDeviceFunction);

        if (!isGpuMethod)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.ExternalBarrierCallProhibited,
                invocation.GetLocation());
        }
    }

    private static void AnalyzeKernelMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
        if (methodSymbol == null)
        {
            return;
        }

        if (methodSymbol.HasAttribute(KnownAttributes.GpuDeviceFunction))
        {
            // GSH0063: the frame of a recursive barrier-reaching device function would contain itself
            if (SuspensionAnalysis.IsRecursiveSuspendingFunction(methodSymbol, semanticModel.Compilation))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.RecursiveDeviceFunctionBarrierProhibited,
                    methodDecl.Identifier.GetLocation(),
                    methodSymbol.Name);
            }

            return;
        }

        if (!methodSymbol.HasAttribute(KnownAttributes.GpuKernel))
        {
            return;
        }

        new KernelBarrierValidator(context, methodDecl, semanticModel).Analyze();
    }

    internal static bool IsSyncThreadsInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax ma)
        {
            return false;
        }

        if (ma.Name.Identifier.ValueText != "SyncThreads")
        {
            return false;
        }

        if (semanticModel.GetSymbol(invocation) is IMethodSymbol symbol)
        {
            return symbol.ContainingType?.Name == TypeConstants.GpuType;
        }

        if (ma.Expression is IdentifierNameSyntax idExpr)
        {
            return idExpr.Identifier.ValueText == TypeConstants.GpuType;
        }

        return false;
    }
}
