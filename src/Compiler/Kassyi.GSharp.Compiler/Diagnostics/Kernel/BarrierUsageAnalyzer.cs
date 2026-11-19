using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Roslyn analyzer that enforces GPU kernel barrier synchronization and state machine constraints (GSH0037 - GSH0047,
/// GSH0061, GSH0062, GSH0083, GSH0084), and the thread divergence of dynamic shared memory placement (GSH0077).
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
            GSharpDiagnostics.DivergentDynamicSharedMemory, // GSH0077
            GSharpDiagnostics.LaunchDependentDivergentBarrier, // GSH0083
            GSharpDiagnostics.BarrierAfterDivergentExit, // GSH0084
            GSharpDiagnostics.GotoEntersReconvergenceRegionMiddle // GSH0120
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

        // The control flow graph and the uniformity analysis, built once for the validators that need them
        var flow = new MethodFlowGraph(semanticModel, methodDecl);
        if (methodSymbol.HasAttribute(KnownAttributes.GpuDeviceFunction))
        {
            // GSH0077: in every device function, as a device function without a barrier may declare one
            DynamicSharedMemoryDivergenceValidator.Analyze(context, methodDecl, semanticModel, flow);

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
                new KernelBarrierValidator(context, methodDecl, semanticModel, isKernel: false, flow).Analyze();
            }

            return;
        }

        if (!methodSymbol.HasAttribute(KnownAttributes.GpuKernel))
        {
            return;
        }

        new KernelBarrierValidator(context, methodDecl, semanticModel, isKernel: true, flow).Analyze();
        DynamicSharedMemoryDivergenceValidator.Analyze(context, methodDecl, semanticModel, flow);
    }

    // GSH0062: the state machine splits only the statements of a kernel or device function body, and the operands that
    // ConditionalEvaluationRewriter turns into statements. A call that suspends anywhere else cannot be resumed, and a
    // warp shuffle anywhere else is not a split point, so a lane would read another lane's value before that lane wrote
    // it, or the shuffle would run where C# does not evaluate it (#1274). A struct member body is not turned into a
    // state machine either; the transpiler reports a suspending call or a warp shuffle there when it writes the body
    // (StructMemberSuspendingCall), so that a member that device code never runs is not reported.
    private static void AnalyzeSuspendingCallContext(SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var (unsupportedContext, shuffleContext) = FindUnsupportedContexts(invocation, context.SemanticModel);
        var byReferenceArgument = FindNonVariableByReferenceArgument(invocation, context.SemanticModel);
        if (unsupportedContext == null && shuffleContext == null && byReferenceArgument == null)
        {
            return;
        }

        // A call that C# removes, or one in its arguments, never runs (RemovedCalls)
        if (context.SemanticModel.GetSymbol(invocation) is not IMethodSymbol callee ||
            !IsInGpuMethod(context.ContainingSymbol) ||
            RemovedCalls.IsInRemovedCall(invocation, context.SemanticModel))
        {
            return;
        }

        if (TryReportWarpShuffleContext(context, invocation, callee, shuffleContext) ||
            !SuspensionAnalysis.IsSuspendingFunction(callee, context.SemanticModel.Compilation))
        {
            return;
        }

        if (unsupportedContext != null)
        {
            context.ReportDiagnostic(GSharpDiagnostics.SuspendingCallInUnsupportedContext, invocation.GetLocation(),
                callee.Name, unsupportedContext, SplitPointPlacement.SuspendingDeviceFunctionCallee);
        }

        if (byReferenceArgument != null)
        {
            // [WHY] The callee is called again on every resume, which would compute the reference again
            context.ReportDiagnostic(GSharpDiagnostics.SuspendingCallInUnsupportedContext,
                byReferenceArgument.GetLocation(),
                callee.Name, "with a by-reference argument that a call, a property or a ref conditional selects",
                SplitPointPlacement.SuspendingDeviceFunctionCallee);
        }
    }

    // The GSH0062 context of the call where a call that suspends, and where a warp shuffle, cannot stand; null where it
    // may. A split that the CPU paths would hoist out of a creation with a collection initializer, before what C#
    // evaluates first, is such a place for both until #2105
    private static (string? Suspending, string? Shuffle) FindUnsupportedContexts(InvocationExpressionSyntax invocation,
        SemanticModel model)
    {
        var initializerContext = CollectionInitializerSplitContext.IsReordered(invocation, model)
            ? CollectionInitializerSplitContext.Context
            : null;
        return (
            SplitPointPlacement.FindUnsupportedContext(invocation, model, isWarpShuffle: false) ?? initializerContext,
            SplitPointPlacement.FindUnsupportedContext(invocation, model, isWarpShuffle: true) ?? initializerContext);
    }

    // [WHY] A shuffle takes its arguments by value, so only where it stands matters. CUDA runs the shuffle where it
    // stands, so a project built only for CUDA, which has no CPU path, accepts it there.
    private static bool TryReportWarpShuffleContext(SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation, IMethodSymbol callee, string? shuffleContext)
    {
        var intrinsic = SyncIntrinsicClassifier.Classify(callee);
        if (intrinsic.Kind != SyncIntrinsicKind.WarpShuffle)
        {
            return false;
        }

        if (shuffleContext != null && BuildsCpuPaths(context.Options))
        {
            context.ReportDiagnostic(GSharpDiagnostics.SuspendingCallInUnsupportedContext,
                invocation.GetLocation(), callee.Name, shuffleContext,
                SplitPointPlacement.GetWarpCallee(intrinsic.Shuffle));
        }

        return true;
    }

    // Whether the project builds the native C++ and managed paths: every dialect but CUDA
    private static bool BuildsCpuPaths(AnalyzerOptions options) =>
        !TranspilerDialects.IsCuda(TranspilerDialects.GetName(options.AnalyzerConfigOptionsProvider.GlobalOptions));

    // [WHY] A suspending call is called again on every resume, and the lowering keeps a by-reference argument as the
    // location it names, evaluating only its array or pointer and its indices once. A reference that a call, a property
    // or a ref conditional selects would be selected again on every resume, so it is reported; the lowering and this
    // rule read the same classification (SpilledArguments.ClassifyByReference). An argument of an in parameter without
    // the keyword is passed by reference too when C# passes the variable itself (#788 review, #1585). #927 is to keep
    // the reference itself instead.
    private static ArgumentSyntax? FindNonVariableByReferenceArgument(InvocationExpressionSyntax invocation,
        SemanticModel semanticModel) =>
        invocation.ArgumentList.Arguments
            .AsStackEnumerable()
            .FirstOrDefault(semanticModel, static (argument, model) =>
                SpilledArguments.ClassifyByReference(argument, model) == ByReferencePassing.ReselectedLocation
            );

    private static bool IsInGpuMethod(ISymbol? symbol) =>
        symbol.GetDeclaringMember() is IMethodSymbol declaring && GpuExecutionSpace.IsGpuFunction(declaring);

    /// <summary>
    /// Whether <paramref name="invocation"/> calls <c>Gpu.SyncThreads()</c>, however it is spelled (<c>Gpu.</c>, a
    /// <c>using</c> alias, <c>using static</c>, the full name), by its symbol (<see cref="SyncIntrinsicClassifier"/>).
    /// </summary>
    internal static bool IsSyncThreadsInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Fast path: a call whose name is not SyncThreads is not resolved
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };

        return name == TypeConstants.SyncThreadsMethod &&
               semanticModel.GetSymbol(invocation) is IMethodSymbol symbol &&
               SyncIntrinsicClassifier.Classify(symbol).Kind == SyncIntrinsicKind.BlockBarrier;
    }
}
