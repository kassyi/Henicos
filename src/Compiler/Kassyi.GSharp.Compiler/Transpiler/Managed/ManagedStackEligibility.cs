using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Eligibility decisions for running kernels and suspending device functions on one OS stack per GPU thread under
/// <c>GSharpManagedBarrierModel=Stack</c>.
/// </summary>
internal static class ManagedStackEligibility
{
    private static readonly PerCompilationSymbolCache<IMethodSymbol, StateMachineReason?> s_functions = new();

    /// <summary>Whether the managed path can run <paramref name="function"/> on the stack of its GPU thread.</summary>
    public static bool IsStackFunction(IMethodSymbol function, Compilation compilation) =>
        FindFunctionReason(function, compilation) is null;

    /// <summary>
    /// Why the managed path keeps <paramref name="function"/> off its thread's stack, or <see langword="null"/> when it
    /// can run on the stack.
    /// </summary>
    public static StateMachineReason? FindFunctionReason(IMethodSymbol function, Compilation compilation)
    {
        var definition = (function.ReducedFrom ?? function).OriginalDefinition;
        definition = definition.PartialDefinitionPart ?? definition;
        var cache = s_functions.For(compilation);
        return cache.GetOrAdd(definition, m => ComputeFunctionReason(m, compilation));
    }

    /// <summary>Whether the managed path can run <paramref name="kernel"/> with one stack per GPU thread.</summary>
    public static bool IsStackKernel(IMethodSymbol kernel, Compilation compilation) =>
        FindKernelReason(kernel, compilation) is null;

    /// <summary>
    /// The first condition that keeps <paramref name="kernel"/> from running on one managed stack per GPU thread, or
    /// <see langword="null"/> when it is eligible.
    /// </summary>
    public static StateMachineReason? FindKernelReason(IMethodSymbol kernel, Compilation compilation)
    {
        if (!kernel.IsStatic)
        {
            return new StateMachineReason(
                CpuPath.Managed,
                "InstanceKernel",
                "it is an instance kernel");
        }

        if (kernel.IsCooperativeKernel())
        {
            return new StateMachineReason(
                CpuPath.Managed,
                nameof(NativeStackObstacle.Cooperative),
                "it is a cooperative kernel (Gpu.SyncGrid)");
        }

        if (FindWarpReason(kernel, compilation) is { } warpReason)
        {
            return warpReason;
        }

        if (!kernel.Parameters
                .AsStackEnumerable()
                .All(CSharpExecutionPlan.SupportsStackArgument))
        {
            return new StateMachineReason(
                CpuPath.Managed,
                "UnsupportedStackArgument",
                "it has a ref struct parameter that the GPU threads cannot share");
        }

        if (!kernel.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            declaration.GetBodyNode() is not { } body)
        {
            return new StateMachineReason(
                CpuPath.Managed,
                "NoMethodBody",
                "its body is not in this compilation");
        }

        var semanticModel = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        var waits = false;
        foreach (var node in RemovedCalls.DescendantNodesThatRun(body, semanticModel))
        {
            if (node is not InvocationExpressionSyntax invocation ||
                semanticModel.GetSymbol(invocation) is not IMethodSymbol callee)
            {
                continue;
            }

            var intrinsic = SyncIntrinsicClassifier.Classify(callee);
            if (intrinsic.Kind == SyncIntrinsicKind.GridBarrier)
            {
                return new StateMachineReason(
                    CpuPath.Managed,
                    nameof(NativeStackObstacle.GridSync),
                    "it uses Gpu.SyncGrid()");
            }

            // A thread waits in its body at a block barrier, a warp collective or a call that reaches one
            if (intrinsic.Kind is SyncIntrinsicKind.BlockBarrier or SyncIntrinsicKind.WarpShuffle ||
                SuspensionAnalysis.IsSuspendingFunction(callee, compilation))
            {
                waits = true;
            }
        }

        if (!waits)
        {
            return new StateMachineReason(
                CpuPath.Managed,
                nameof(NativeStackObstacle.NoBlockBarrier),
                "it does not reach a barrier or warp collective");
        }

        foreach (var callee in SuspensionAnalysis.GetSuspendingCallees(body, compilation))
        {
            if (FindFunctionReason(callee, compilation) is { } calleeReason)
            {
                return calleeReason;
            }
        }

        return null;
    }

    /// <summary>
    /// Why <paramref name="kernel"/>, which reaches a warp collective (<c>Warp.ActiveMask()</c> included), cannot run
    /// its collectives on the managed stack model; <see langword="null"/> when it can, or when it reaches none.
    /// </summary>
    public static string? GetWarpFailure(IMethodSymbol kernel, Compilation compilation) =>
        FindWarpReason(kernel, compilation)?.Sentence;

    private static StateMachineReason? FindWarpReason(IMethodSymbol kernel, Compilation compilation)
    {
        if (!SuspensionAnalysis.ReachesWarpShuffle(kernel, compilation))
        {
            return null;
        }

        if (!WarpFrameLayout.TryGet(kernel, compilation, out var layout))
        {
            return new StateMachineReason(
                CpuPath.Managed,
                nameof(NativeStackObstacle.WarpLayout),
                "the visit counts of its warp collectives cannot be placed (a device function of another assembly or a " +
                "recursive one reaches a collective)");
        }

        if (layout.Size > WarpFrameLayout.MaxSize)
        {
            return new StateMachineReason(
                CpuPath.Managed,
                nameof(NativeStackObstacle.WarpFrameTooLarge),
                $"its warp collectives need {layout.Size} visit counts per thread, more than {WarpFrameLayout.MaxSize}");
        }

        if (StackBodyObstacles.DeclaresUsingWithActiveMask(kernel, compilation))
        {
            return new StateMachineReason(
                CpuPath.Managed,
                nameof(NativeStackObstacle.UsingDeclarationWithActiveMask),
                "it reaches Warp.ActiveMask() and declares a local with using: a jump of its reconvergence regions " +
                "may pass the declaration");
        }

        return null;
    }

    private static StateMachineReason? ComputeFunctionReason(IMethodSymbol function, Compilation compilation)
    {
        if (!SuspensionAnalysis.TryGetDeviceFunctionBody(function, compilation, out var declaration) ||
            declaration.GetBodyNode() is not { } body)
        {
            return SuspensionAnalysis.IsSuspendingFunction(function, compilation)
                ? new StateMachineReason(
                    CpuPath.Managed,
                    nameof(NativeStackObstacle.ImportedSuspendingCall),
                    "it calls a suspending device function from another assembly")
                : new StateMachineReason(
                    CpuPath.Managed,
                    nameof(NativeStackObstacle.NoBlockBarrier),
                    "it does not reach a barrier or warp collective");
        }

        if (!SuspensionAnalysis.RequiresStateMachine(function, compilation))
        {
            return SuspensionAnalysis.IsSuspendingFunction(function, compilation)
                ? new StateMachineReason(
                    CpuPath.Managed,
                    nameof(NativeStackObstacle.RecursiveSuspendingCall),
                    "it reaches a recursive suspending device function")
                : new StateMachineReason(
                    CpuPath.Managed,
                    nameof(NativeStackObstacle.NoBlockBarrier),
                    "it does not reach a barrier or warp collective");
        }

        if (SuspensionAnalysis.ReachesGridBarrier(function, compilation))
        {
            return new StateMachineReason(
                CpuPath.Managed,
                nameof(NativeStackObstacle.GridSync),
                "it uses Gpu.SyncGrid()");
        }

        if (!function.IsStatic)
        {
            return new StateMachineReason(
                CpuPath.Managed,
                "InstanceDeviceFunction",
                "it reaches an instance suspending device function");
        }

        if (!ManagedStateMachineAvailability.AreContainingTypesPartial(function))
        {
            return new StateMachineReason(
                CpuPath.Managed,
                "NonPartialContainingType",
                "a containing type of a suspending device function is not partial");
        }

        if (FileLocalNames.BlocksCopy(function, compilation))
        {
            return new StateMachineReason(
                CpuPath.Managed,
                "FileLocalType",
                "a suspending device function's copy names a file-local type");
        }

        if (SuspensionAnalysis.ReachesWarpShuffle(function, compilation) &&
            !WarpFrameLayout.TryGet(function, compilation, out _))
        {
            return new StateMachineReason(
                CpuPath.Managed,
                nameof(NativeStackObstacle.WarpLayout),
                "the visit counts of its warp collectives cannot be placed (a device function of another assembly or a " +
                "recursive one reaches a collective)");
        }

        if (StackBodyObstacles.DeclaresUsingWithActiveMask(function, compilation))
        {
            return new StateMachineReason(
                CpuPath.Managed,
                nameof(NativeStackObstacle.UsingDeclarationWithActiveMask),
                "it reaches Warp.ActiveMask() and declares a local with using: a jump of its reconvergence regions " +
                "may pass the declaration");
        }

        foreach (var callee in SuspensionAnalysis.GetSuspendingCallees(body, compilation))
        {
            if (FindFunctionReason(callee, compilation) is { } calleeReason)
            {
                return calleeReason;
            }
        }

        return null;
    }
}
