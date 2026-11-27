using Kassyi.GSharp.Compiler.Instantiation;
using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Eligibility decisions for running kernels and suspending device functions on one OS stack per GPU thread under
/// <c>GSharpManagedBarrierModel=Stack</c>.
/// </summary>
internal static class ManagedStackEligibility
{
    private const string WarpLayoutSentence =
        "the visit counts of its warp collectives cannot be placed (a device function of another assembly or a " +
        "recursive one reaches a collective)";

    private const string NoBarrierSentence = "it does not reach a barrier or warp collective";

    private const string StructMemberSplitSentence =
        "it reaches a barrier or warp collective through a struct member";

    private static readonly PerCompilationSymbolCache<IMethodSymbol, StateMachineReason?> s_functions = new();

    /// <summary>Whether the managed path can run <paramref name="function"/> on the stack of its GPU thread.</summary>
    public static bool IsStackFunction(IMethodSymbol function, Compilation compilation) =>
        FindFunctionReason(function, compilation) is null;

    /// <summary>
    /// Why the managed path keeps <paramref name="function"/> off its thread's stack, or <see langword="null"/> when it
    /// can run on the stack.
    /// </summary>
    public static StateMachineReason? FindFunctionReason(IMethodSymbol function, Compilation compilation) =>
        FindFunctionReason(function, compilation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));

    private static StateMachineReason? FindFunctionReason(IMethodSymbol function, Compilation compilation,
        HashSet<IMethodSymbol> visiting)
    {
        var definition = (function.ReducedFrom ?? function).OriginalDefinition;
        definition = definition.PartialDefinitionPart ?? definition;
        var cache = s_functions.For(compilation);
        if (cache.TryGetValue(definition, out var cached))
        {
            return cached;
        }

        // A cycle through method groups (F passes F, or F and G pass each other)
        if (!visiting.Add(definition))
        {
            return Managed(nameof(NativeStackObstacle.RecursiveSuspendingCall),
                "it reaches a recursive suspending device function");
        }

        StateMachineReason? reason;
        try
        {
            reason = ComputeFunctionReason(definition, compilation, visiting);
        }
        finally
        {
            visiting.Remove(definition);
        }

        cache.TryAdd(definition, reason);
        return reason;
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
            return Managed("InstanceKernel", "it is an instance kernel");
        }

        if (FindWarpReason(kernel, compilation) is { } warpReason)
        {
            return warpReason;
        }

        if (!kernel.Parameters
                .AsStackEnumerable()
                .All(CSharpExecutionPlan.SupportsStackArgument))
        {
            return Managed("UnsupportedStackArgument",
                "it has a ref struct parameter that the GPU threads cannot share");
        }

        if (!kernel.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            declaration.GetBodyNode() is not { } body)
        {
            return Managed("NoMethodBody", "its body is not in this compilation");
        }

        var semanticModel = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        return FindBodyReason(body, kernel, compilation, semanticModel) ??
               FindCalleeReason(body, compilation, semanticModel, null);
    }

    // A body in which no thread ever waits, or a struct member reaching a split
    private static StateMachineReason? FindBodyReason(SyntaxNode body, IMethodSymbol kernel, Compilation compilation,
        SemanticModel semanticModel)
    {
        var waits = false;
        foreach (var node in RemovedCalls.DescendantNodesThatRun(body, semanticModel))
        {
            if (node is not InvocationExpressionSyntax invocation ||
                semanticModel.GetSymbol(invocation) is not IMethodSymbol callee)
            {
                continue;
            }

            var intrinsic = SyncIntrinsicClassifier.Classify(callee);
            // A thread waits in its body at a block barrier, a grid barrier, a warp collective or a call that reaches one
            if (intrinsic.Kind is SyncIntrinsicKind.BlockBarrier or SyncIntrinsicKind.GridBarrier
                    or SyncIntrinsicKind.WarpShuffle ||
                SuspensionAnalysis.IsSuspendingFunction(callee, compilation))
            {
                waits = true;
            }
        }

        if (SuspensionAnalysis.HasStructMemberSplit(body, TypeArgumentMap.Of(kernel), compilation))
        {
            return Managed(nameof(NativeStackObstacle.StructMemberSplit), StructMemberSplitSentence);
        }

        return waits ? null : Managed(nameof(NativeStackObstacle.NoBlockBarrier), NoBarrierSentence);
    }

    // The reason of the first suspending function that the body calls or names as a method group
    private static StateMachineReason? FindCalleeReason(SyntaxNode body, Compilation compilation,
        SemanticModel semanticModel, HashSet<IMethodSymbol>? visiting)
    {
        foreach (var (callee, isMethodGroup) in
                 SuspensionAnalysis.GetReferencedSuspendingFunctions(body, compilation, semanticModel))
        {
            var reason = visiting is null
                ? FindFunctionReason(callee, compilation)
                : FindFunctionReason(callee, compilation, visiting);
            if (reason is not null)
            {
                return !SuspensionAnalysis.TryGetDeviceFunctionBody(callee, compilation, out _) &&
                       reason.Value.Name == nameof(NativeStackObstacle.ImportedSuspendingCall)
                    ? CreateImportedMissingFormReason(callee, compilation, isMethodGroup)
                    : reason;
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
        if (!SuspensionAnalysis.ReachesWarpShuffleOnStack(kernel, compilation))
        {
            return null;
        }

        if (!WarpFrameLayout.TryGet(kernel, compilation, out var layout))
        {
            return Managed(nameof(NativeStackObstacle.WarpLayout), WarpLayoutSentence);
        }

        if (layout.Size > WarpFrameLayout.MaxSize)
        {
            return Managed(nameof(NativeStackObstacle.WarpFrameTooLarge),
                $"its warp collectives need {layout.Size} visit counts per thread, more than " +
                $"{WarpFrameLayout.MaxSize}");
        }

        return null;
    }

    private static StateMachineReason? ComputeFunctionReason(IMethodSymbol function, Compilation compilation,
        HashSet<IMethodSymbol> visiting)
    {
        var suspends = SuspensionAnalysis.IsSuspendingFunction(function, compilation);
        if (!SuspensionAnalysis.TryGetDeviceFunctionBody(function, compilation, out var declaration))
        {
            if (!suspends)
            {
                return Managed(nameof(NativeStackObstacle.NoBlockBarrier), NoBarrierSentence);
            }

            return ImportedStackForms.HasManagedForm(function, compilation)
                ? null
                : CreateImportedMissingFormReason(function, compilation, isMethodGroup: false);
        }

        if (declaration.GetBodyNode() is not { } body)
        {
            return suspends
                ? Managed("NoMethodBody", "its body is not in this compilation")
                : Managed(nameof(NativeStackObstacle.NoBlockBarrier), NoBarrierSentence);
        }

        if (GenericDefinitions.IsLeftOut(function, compilation))
        {
            return Managed("GenericDeviceFunction", "it is a generic device function");
        }

        if (!SuspensionAnalysis.RequiresStateMachine(function, compilation))
        {
            return suspends
                ? Managed(nameof(NativeStackObstacle.RecursiveSuspendingCall),
                    "it reaches a recursive suspending device function")
                : Managed(nameof(NativeStackObstacle.NoBlockBarrier), NoBarrierSentence);
        }

        if (FindFunctionShapeReason(function, compilation) is { } shapeReason)
        {
            return shapeReason;
        }

        if (SuspensionAnalysis.HasStructMemberSplit(body, TypeArgumentMap.Of(function), compilation))
        {
            return Managed(nameof(NativeStackObstacle.StructMemberSplit), StructMemberSplitSentence);
        }

        var semanticModel = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        return FindCalleeReason(body, compilation, semanticModel, visiting);
    }

    // What the function itself has that the managed stack form cannot copy or run
    private static StateMachineReason? FindFunctionShapeReason(IMethodSymbol function, Compilation compilation)
    {
        if (!function.IsStatic)
        {
            return Managed("InstanceDeviceFunction", "it reaches an instance suspending device function");
        }

        if (!ManagedStateMachineAvailability.AreContainingTypesPartial(function))
        {
            return Managed("NonPartialContainingType",
                "a containing type of a suspending device function is not partial");
        }

        if (FileLocalNames.BlocksCopy(function, compilation))
        {
            return Managed("FileLocalType", "a suspending device function's copy names a file-local type");
        }

        if (SuspensionAnalysis.ReachesWarpShuffleOnStack(function, compilation) &&
            !WarpFrameLayout.TryGet(function, compilation, out _))
        {
            return Managed(nameof(NativeStackObstacle.WarpLayout), WarpLayoutSentence);
        }

        return null;
    }

    /// <summary>
    /// Builds the fallback reason for an imported suspending device function without a managed stack form.
    /// </summary>
    internal static StateMachineReason CreateImportedMissingFormReason(IMethodSymbol callee, Compilation compilation,
        bool isMethodGroup)
    {
        var assemblyName = DeviceExportTable.TryFindFunction(callee, compilation, out var imported)
            ? imported.AssemblyName
            : callee.ContainingAssembly?.Name ?? "";
        var displayName = KernelHostNames.GetMessageName(callee);
        var detail = isMethodGroup
            ? $"passes {displayName} of {assemblyName}, which has no stack form, as a method group"
            : $"calls {displayName} of {assemblyName}, which has no stack form";
        var sentence = isMethodGroup
            ? "it passes a suspending device function from another assembly as a method group"
            : "it calls a suspending device function from another assembly";
        return new StateMachineReason(
            CpuPath.Managed,
            nameof(NativeStackObstacle.ImportedSuspendingCall),
            sentence,
            detail);
    }

    private static StateMachineReason Managed(string name, string sentence) => new(CpuPath.Managed, name, sentence);
}
