namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Roslyn analyzer that enforces GPU kernel barrier synchronization and state machine constraints (GSH0037 - GSH0047,
/// GSH0061, GSH0062), and the thread divergence of dynamic shared memory placement (GSH0077).
/// Optimized for minimal AST passes, zero-allocation traversal, and fast-path exits.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BarrierUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.UnsupportedSyntax, // GSH0003 (a barrier in the scope of a using)
            GSharpDiagnostics.DivergentBarrierProhibited, // GSH0037
            GSharpDiagnostics.ExceptionHandlingInKernelProhibited, // GSH0038
            GSharpDiagnostics.CrossBarrierRefStructProhibited, // GSH0039
            GSharpDiagnostics.CrossBarrierGotoProhibited, // GSH0040
            GSharpDiagnostics.CrossBarrierLocalPointerProhibited, // GSH0041
            GSharpDiagnostics.ExternalBarrierCallProhibited, // GSH0042
            GSharpDiagnostics.FixedBlockBarrierProhibited, // GSH0043
            GSharpDiagnostics.RecursiveBarrierProhibited, // GSH0045
            GSharpDiagnostics.AsyncYieldInKernelProhibited, // GSH0046
            GSharpDiagnostics.LockStatementInKernelProhibited, // GSH0047
            GSharpDiagnostics.RecursiveDeviceFunctionBarrierProhibited, // GSH0061
            GSharpDiagnostics.SuspendingCallInUnsupportedContext, // GSH0062
            GSharpDiagnostics.IteratorShapeNotSupported, // GSH0070
            GSharpDiagnostics.IteratorBodyConstructNotSupported, // GSH0072
            GSharpDiagnostics.DivergentDynamicSharedMemory // GSH0077
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
            AnalyzeSuspendingCallContext(context, invocation);
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

        if (!GpuExecutionSpace.IsGpuFunction(enclosingMethod))
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
            // GSH0077: in every device function, as a device function without a barrier may declare one
            DynamicSharedMemoryDivergenceValidator.Analyze(context, methodDecl, semanticModel);

            // GSH0061: the frame of a recursive barrier-reaching device function would contain itself
            if (SuspensionAnalysis.IsRecursiveSuspendingFunction(methodSymbol, semanticModel.Compilation))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.RecursiveDeviceFunctionBarrierProhibited,
                    methodDecl.Identifier.GetLocation(),
                    methodSymbol.Name);
            }

            // A device function that reaches a barrier or is an iterator is validated for state machine / iterator
            // rules
            if (SuspensionAnalysis.IsSuspendingFunction(methodSymbol, semanticModel.Compilation) ||
                IteratorTypeResolver.ContainsAnyYield(methodDecl.Body))
            {
                new KernelBarrierValidator(context, methodDecl, semanticModel, isKernel: false).Analyze();
            }

            return;
        }

        if (!methodSymbol.HasAttribute(KnownAttributes.GpuKernel))
        {
            return;
        }

        new KernelBarrierValidator(context, methodDecl, semanticModel, isKernel: true).Analyze();
        DynamicSharedMemoryDivergenceValidator.Analyze(context, methodDecl, semanticModel);
    }

    // GSH0062: the state machine splits only the statements of a kernel or device function body, and the operands that
    // ConditionalEvaluationRewriter turns into statements. A call that suspends anywhere else cannot be resumed.
    private static void AnalyzeSuspendingCallContext(SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var unsupportedContext = FindUnsupportedContext(invocation);
        var byReferenceArgument = FindNonVariableByReferenceArgument(invocation);
        if (unsupportedContext == null && byReferenceArgument == null)
        {
            return;
        }

        if (context.SemanticModel.GetSymbol(invocation) is not IMethodSymbol callee ||
            !SuspensionAnalysis.IsSuspendingFunction(callee, context.SemanticModel.Compilation) ||
            !IsInGpuMethod(context.ContainingSymbol))
        {
            return;
        }

        if (unsupportedContext != null)
        {
            context.ReportDiagnostic(GSharpDiagnostics.SuspendingCallInUnsupportedContext, invocation.GetLocation(),
                callee.Name, unsupportedContext);
        }

        if (byReferenceArgument != null)
        {
            // [WHY] The callee is called again on every resume, which would compute the reference again
            context.ReportDiagnostic(GSharpDiagnostics.SuspendingCallInUnsupportedContext,
                byReferenceArgument.GetLocation(),
                callee.Name, "with a by-reference argument that is not a local, parameter, field or array element");
        }
    }

    private static string? FindUnsupportedContext(InvocationExpressionSyntax invocation)
    {
        SyntaxNode child = invocation;
        for (var ancestor = invocation.Parent;
             ancestor is not (null or MemberDeclarationSyntax);
             child = ancestor, ancestor = ancestor.Parent)
        {
            switch (ancestor)
            {
                // The transpiler inlines the body (CUDA), and the lowering inlines a SyncGrid body and turns ForUnroll into a
                // loop (CPU dialects, managed)
                case AnonymousFunctionExpressionSyntax when IsInlinedIntrinsicArgument(ancestor):
                    break;
                case AnonymousFunctionExpressionSyntax:
                    return "in a lambda or anonymous method";
                case LocalFunctionStatementSyntax:
                    return "in a local function";
                case SwitchExpressionArmSyntax:
                    return "in a switch expression arm";
                case WhenClauseSyntax:
                    return "in a 'when' clause";
                case ConditionalAccessExpressionSyntax access when child == access.WhenNotNull:
                    return "after '?.'";
                case BinaryExpressionSyntax binary
                    when child == binary.Right && binary.IsKind(SyntaxKind.CoalesceExpression):
                    return "as the right operand of '??'";
            }
        }

        return null;
    }

    private static bool IsInlinedIntrinsicArgument(SyntaxNode lambda) =>
        lambda.Parent is ArgumentSyntax
        {
            Parent.Parent: InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: TypeConstants.GpuType },
                    Name.Identifier.ValueText: TypeConstants.GpuMacros.SyncGrid or "ForUnroll"
                }
            }
        };

    private static ArgumentSyntax? FindNonVariableByReferenceArgument(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments
            .AsStackEnumerable()
            .FirstOrDefault(static argument =>
                !argument.RefKindKeyword.IsKind(SyntaxKind.None) &&
                argument.Expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax
                    or ElementAccessExpressionSyntax or DeclarationExpressionSyntax)
            );

    private static bool IsInGpuMethod(ISymbol? symbol)
    {
        // Lambdas and local functions belong to the method that declares them
        while (symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
        {
            symbol = symbol.ContainingSymbol;
        }

        return symbol is IMethodSymbol && GpuExecutionSpace.IsGpuFunction(symbol);
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
            return symbol.ContainingType is { Name: TypeConstants.GpuType } gpuType &&
                   gpuType.ContainingNamespace.IsCoreNamespace();
        }

        if (ma.Expression is IdentifierNameSyntax idExpr)
        {
            return idExpr.Identifier.ValueText == TypeConstants.GpuType;
        }

        return false;
    }
}
