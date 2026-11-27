using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Finds the <c>[GpuDeviceFunction]</c> methods that reach a phase barrier, directly or through other device functions.
/// </summary>
/// <remarks>
/// On the CPU dialects such a function suspends its caller, so it is lowered into its own state machine and every call
/// to it becomes a suspension point. A barrier is an intrinsic that splits the state machine
/// (<see cref="SyncIntrinsicClassifier"/>, by symbol), so a device function suspends exactly when kernel-level lowering
/// would split on the same code.
/// <para>
/// A device function of another assembly has no body in the compilation; its assembly recorded the answers
/// (<see cref="DeviceExportTable"/>, #538). One without a record is taken as not reaching a barrier, and the analyzer
/// reports the call (GSH0088), so the build does not go on with that guess.
/// </para>
/// </remarks>
internal static class SuspensionAnalysis
{
    // Any intrinsic that splits the state machine, also a warp shuffle
    private static readonly BarrierQuery s_splitting = new(
        alsoKernels: false,
        static intrinsic => intrinsic.SplitsStateMachine,
        static (traits, _) => traits.Suspends
    );

    // Gpu.SyncThreads() and Gpu.SyncGrid(), which every thread of the block must reach
    private static readonly BarrierQuery s_block = new(
        alsoKernels: false,
        static intrinsic => intrinsic.Kind is SyncIntrinsicKind.BlockBarrier or SyncIntrinsicKind.GridBarrier,
        static (traits, _) => traits.ReachesBlockBarrier
    );

    // Gpu.SyncGrid(), which every thread of the grid must reach. The export record does not say whether a device
    // function of another assembly reaches one, so such a call is taken as reaching a block barrier only
    private static readonly BarrierQuery s_grid = new(
        alsoKernels: false,
        static intrinsic => intrinsic.Kind == SyncIntrinsicKind.GridBarrier,
        static (_, _) => false
    );

    // Warp.Shuffle*Sync and Gpu.Match*Sync. The export record says only whether the function suspends
    private static readonly BarrierQuery s_warpShuffle = new(
        alsoKernels: true,
        static intrinsic => intrinsic.Kind == SyncIntrinsicKind.WarpShuffle,
        static (traits, _) => traits.Suspends
    );

    // Warp.ActiveMask(). The export record does not say whether the function reaches it; one that suspends is taken
    // as reaching it
    private static readonly BarrierQuery s_activeMask = new(
        alsoKernels: true,
        static intrinsic => intrinsic.Shuffle == WarpShuffleKind.ActiveMask,
        static (traits, _) => traits.Suspends
    );

    // Gpu.SyncGrid(), for the CPU stack models: answered from the export record when compatible, else Suspends
    private static readonly BarrierQuery s_gridOnStack = new(
        alsoKernels: false,
        static intrinsic => intrinsic.Kind == SyncIntrinsicKind.GridBarrier,
        static (traits, isCompatible) => isCompatible ? traits.ReachesGridBarrier : traits.Suspends
    );

    // Warp.Shuffle*Sync and Gpu.Match*Sync, for the CPU stack models: answered from the export record when compatible,
    // else Suspends
    private static readonly BarrierQuery s_warpShuffleOnStack = new(
        alsoKernels: true,
        static intrinsic => intrinsic.Kind == SyncIntrinsicKind.WarpShuffle,
        static (traits, isCompatible) => isCompatible ? traits.ReachesWarpShuffle : traits.Suspends
    );

    // Warp.ActiveMask(), for the CPU stack models: answered from the export record when compatible, else Suspends
    private static readonly BarrierQuery s_activeMaskOnStack = new(
        alsoKernels: true,
        static intrinsic => intrinsic.Shuffle == WarpShuffleKind.ActiveMask,
        static (traits, isCompatible) => isCompatible ? traits.ReachesActiveMask : traits.Suspends
    );

    // Gpu.SyncGrid() and the mbarrier intrinsics, which wait for a fixed number of arrivals
    private static readonly BarrierQuery s_arrival = new(
        alsoKernels: true,
        static intrinsic => intrinsic.Kind is SyncIntrinsicKind.GridBarrier or SyncIntrinsicKind.AsyncBarrier,
        static (traits, _) => traits.ReachesArrivalBarrier
    );

    /// <summary>Whether <paramref name="method"/> is a device function that reaches a phase barrier.</summary>
    public static bool IsSuspendingFunction(IMethodSymbol method, Compilation compilation) =>
        Reaches(method, s_splitting, compilation);

    /// <summary>
    /// Whether <paramref name="method"/> is a device function that reaches a block barrier (<c>Gpu.SyncThreads()</c> or
    /// <c>Gpu.SyncGrid()</c>), directly or through other device functions.
    /// </summary>
    /// <remarks>
    /// [WHY] Every thread of the block must reach a block barrier, so a call to such a function must not sit in a branch
    /// (GSH0037). A warp shuffle takes the mask of the threads that reach it, so a call that reaches only shuffles may.
    /// </remarks>
    public static bool ReachesBlockBarrier(IMethodSymbol method, Compilation compilation) =>
        Reaches(method, s_block, compilation);

    /// <summary>
    /// Whether <paramref name="method"/> is a device function that reaches a grid barrier (<c>Gpu.SyncGrid()</c>),
    /// directly or through other device functions.
    /// </summary>
    /// <remarks>
    /// [WHY] Every thread of the grid must reach a grid barrier, so a call to such a function diverges also on a branch
    /// that varies only between blocks (GSH0083). The export record of a device function of another assembly does not
    /// say which barrier it reaches; such a call is taken as reaching a block barrier only.
    /// </remarks>
    public static bool ReachesGridBarrier(IMethodSymbol method, Compilation compilation) =>
        Reaches(method, s_grid, compilation);

    /// <summary>
    /// Whether <paramref name="method"/> is a device function that reaches a grid barrier (<c>Gpu.SyncGrid()</c>),
    /// directly or through other device functions, answered for the CPU stack models.
    /// </summary>
    /// <remarks>
    /// These are the answers the stack models use; they equal <see cref="ReachesGridBarrier"/> for methods that call
    /// no device function of another assembly.
    /// </remarks>
    public static bool ReachesGridBarrierOnStack(IMethodSymbol method, Compilation compilation) =>
        Reaches(method, s_gridOnStack, compilation);

    /// <summary>
    /// The barrier scope of a call to <paramref name="method"/>, a device function that reaches a block barrier: the grid
    /// when it also reaches a grid barrier.
    /// </summary>
    public static SyncScope GetBarrierScope(IMethodSymbol method, Compilation compilation) =>
        ReachesGridBarrier(method, compilation) ? SyncScope.Grid : SyncScope.Block;

    /// <summary>
    /// Whether <paramref name="method"/> (a kernel or a device function) reaches a warp shuffle or warp match
    /// (<see cref="SyncIntrinsicKind.WarpShuffle"/>), directly or through device functions it calls.
    /// </summary>
    public static bool ReachesWarpShuffle(IMethodSymbol method, Compilation compilation) =>
        Reaches(method, s_warpShuffle, compilation);

    /// <summary>
    /// Whether <paramref name="method"/> (a kernel or a device function) reaches a warp shuffle or warp match
    /// (<see cref="SyncIntrinsicKind.WarpShuffle"/>), directly or through device functions it calls, answered for the
    /// CPU stack models.
    /// </summary>
    /// <remarks>
    /// These are the answers the stack models use; they equal <see cref="ReachesWarpShuffle"/> for methods that call
    /// no device function of another assembly.
    /// </remarks>
    public static bool ReachesWarpShuffleOnStack(IMethodSymbol method, Compilation compilation) =>
        Reaches(method, s_warpShuffleOnStack, compilation);

    /// <summary>
    /// Whether <paramref name="method"/> (a kernel or a device function) reaches a barrier that waits for a fixed
    /// number of arrivals (<c>Gpu.SyncGrid()</c>, an mbarrier), directly or through device functions it calls.
    /// </summary>
    /// <remarks>
    /// [WHY] A thread that failed and exited never arrives, so the waiters of such a barrier leave on the abort word of
    /// their launch; a launch captured into a CUDA graph gets a word only when its kernel reaches one
    /// (<c>LaunchErrorRecords.NextLaunchEpoch</c>). Any mbarrier intrinsic counts: a kernel that initializes or arrives
    /// at one waits at it too.
    /// </remarks>
    public static bool ReachesArrivalBarrier(IMethodSymbol method, Compilation compilation) =>
        Reaches(method, s_arrival, compilation);

    /// <summary>
    /// Whether <paramref name="method"/> (a kernel or a device function) reaches <c>Warp.ActiveMask()</c>, directly or
    /// through device functions it calls.
    /// </summary>
    /// <remarks>
    /// [WHY] The CPU paths make the lanes of a warp that took different branches wait for each other where the branches
    /// meet again, so that <c>Warp.ActiveMask()</c> sees the lanes that reach it together (#844); that costs launcher
    /// rounds, so only such methods do it. A device function of another assembly does not record whether it reaches
    /// <c>Warp.ActiveMask()</c>; one that suspends is taken as reaching it, so that the kernel context has room for its
    /// branches.
    /// </remarks>
    public static bool ReachesActiveMask(IMethodSymbol method, Compilation compilation) =>
        Reaches(method, s_activeMask, compilation);

    /// <summary>
    /// Whether <paramref name="method"/> (a kernel or a device function) reaches <c>Warp.ActiveMask()</c>, directly or
    /// through device functions it calls, answered for the CPU stack models.
    /// </summary>
    /// <remarks>
    /// These are the answers the stack models use; they equal <see cref="ReachesActiveMask"/> for methods that call
    /// no device function of another assembly.
    /// </remarks>
    public static bool ReachesActiveMaskOnStack(IMethodSymbol method, Compilation compilation) =>
        Reaches(method, s_activeMaskOnStack, compilation);

    /// <summary>
    /// Whether <paramref name="method"/> reaches a phase barrier and calls itself, directly or through other device
    /// functions. Its frame would have to contain itself (GSH0061).
    /// </summary>
    public static bool IsRecursiveSuspendingFunction(IMethodSymbol method, Compilation compilation)
    {
        var definition = ToCanonicalDefinitionPart((method.ReducedFrom ?? method).OriginalDefinition);
        return IsSuspendingFunction(method, compilation) &&
               CallsReach(definition, definition, compilation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a device function that the CPU dialects lower into a state machine: one that
    /// reaches a phase barrier and is not recursive. A recursive one is rejected (GSH0061), so no state machine is generated for it.
    /// </summary>
    public static bool RequiresStateMachine(IMethodSymbol method, Compilation compilation) =>
        IsSuspendingFunction(method, compilation) && !IsRecursiveSuspendingFunction(method, compilation);

    /// <summary>
    /// The device functions that reach a phase barrier and are called from <paramref name="body"/>, in call order.
    /// Does not follow struct members: struct members have no state-machine frame (<c>DeviceFunctionForms.Create</c>,
    /// <c>ManagedStateMachineAvailability</c>, <c>ManagedStackForms</c>, and stack eligibility callers).
    /// </summary>
    public static ImmutableArray<IMethodSymbol> GetSuspendingCallees(SyntaxNode body, Compilation compilation)
    {
        var callees = ImmutableArray.CreateBuilder<IMethodSymbol>();
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var callee in GetDeviceFunctionCallees(body, compilation))
        {
            if (IsSuspendingFunction(callee, compilation) && seen.Add(callee))
            {
                callees.Add(callee);
            }
        }

        return callees.ToImmutable();
    }

    /// <summary>
    /// The <c>[GpuDeviceFunction]</c> methods that reach a phase barrier and are called or named as a method group in
    /// <paramref name="body"/>, outside calls that C# removes (<see cref="RemovedCalls"/>) and <c>nameof</c>
    /// arguments, with repeats.
    /// </summary>
    internal static IEnumerable<(IMethodSymbol Definition, bool IsMethodGroup)> GetReferencedSuspendingFunctions(
        SyntaxNode body,
        Compilation compilation,
        SemanticModel semanticModel)
    {
        foreach (var node in RemovedCalls.DescendantNodesThatRun(body, semanticModel))
        {
            if (node is not SimpleNameSyntax || NameofOperator.Encloses(node, semanticModel))
            {
                continue;
            }

            if (semanticModel.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
            {
                continue;
            }

            var definition = (method.ReducedFrom ?? method).OriginalDefinition;
            definition = definition.PartialDefinitionPart ?? definition;
            if (HasDeviceFunctionAttribute(definition) &&
                IsSuspendingFunction(definition, compilation))
            {
                yield return (definition, MethodGroupReference.Is(node, semanticModel));
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="body"/> directly calls a member of an emitted struct (not a <c>[GpuDeviceFunction]</c>)
    /// that reaches a split point (block barrier, grid barrier, warp collective, or suspending device function).
    /// </summary>
    internal static bool HasStructMemberSplit(SyntaxNode body, TypeArgumentMap typeArguments, Compilation compilation)
    {
        var semanticModel = compilation.GetCachedSemanticModel(body.SyntaxTree);
        foreach (var site in DirectCallees.Of([body], typeArguments, compilation))
        {
            if (!HasDeviceFunctionAttribute(site.Callee) &&
                IsEmittedStructMember(site.Callee) &&
                !DirectCallees.IsMethodGroupSite(site.Site, site.Callee, semanticModel) &&
                IsSuspendingFunction(site.Callee, compilation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Reaches(IMethodSymbol method, BarrierQuery query, Compilation compilation)
    {
        var target = ToCanonicalDefinitionPart(method.GetConstructedReducedFrom() ?? method);
        return TryGetReachableBodies(target, query.AlsoKernels, compilation, out _)
            ? ReachesCached(target, query, compilation)
            : ImportedReaches(target, query, compilation);
    }

    // Normalizes a partial method symbol (including a constructed implementing part, where Roslyn leaves
    // PartialDefinitionPart null) to the defining declaration symbol with the same type arguments.
    private static IMethodSymbol ToCanonicalDefinitionPart(IMethodSymbol method)
    {
        if (method.PartialDefinitionPart is { } directDef)
        {
            return directDef;
        }

        if (method.OriginalDefinition.PartialDefinitionPart is not { } defOrig)
        {
            return method;
        }

        var defMember = method.ContainingType is { IsGenericType: true } containing
            ? containing.GetMembers(defOrig.Name)
                .AsStackEnumerable()
                .OfType<IMethodSymbol>()
                .FirstOrDefault(defOrig, static (m, d) => m.OriginalDefinition.IsEqualTo(d)) ?? defOrig
            : defOrig;
        if (!method.IsGenericMethod || defMember.TypeParameters.Length != method.TypeArguments.Length)
        {
            return defMember;
        }

        var typeArguments = new ITypeSymbol[method.TypeArguments.Length];
        method.TypeArguments.CopyTo(typeArguments);
        return defMember.ConstructedFrom.Construct(typeArguments);
    }

    // [WHY] Only the queried function is cached: a depth-first walk that stops at an already visited function would
    // record a wrong 'false' for the functions inside a cycle.
    private static bool ReachesCached(IMethodSymbol target, BarrierQuery query, Compilation compilation)
    {
        var answers = query.Answers.For(compilation);
        if (!answers.TryGetValue(target, out var reaches))
        {
            reaches = ReachesBarrier(
                target,
                query,
                compilation,
                caller: null,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)
            );
            answers.TryAdd(target, reaches);
        }

        return reaches;
    }

    // A device function of another assembly: what its assembly recorded
    private static bool ImportedReaches(IMethodSymbol method, BarrierQuery query, Compilation compilation) =>
        DeviceExportTable.TryFindFunction(method, compilation, out var imported) &&
        query.Imported(imported.Record.Traits, imported.IsCompatible);

    // One walk of the body: a barrier answers at once, and a device function or emitted struct member it calls is walked
    // where it is called. The answer for the queried function does not depend on that order: a walk that stops at a
    // function already on the path leaves the rest of that function to the walk that is still running
    private static bool ReachesBarrier(IMethodSymbol method, BarrierQuery query, Compilation compilation,
        ReachStep? caller, HashSet<IMethodSymbol> visited)
    {
        if (StopsPolymorphicRecursion(method, caller, compilation) || !visited.Add(method))
        {
            return false;
        }

        if (!TryGetReachableBodies(method, alsoKernels: true, compilation, out var bodies))
        {
            return ImportedReaches(method, query, compilation);
        }

        foreach (var body in bodies)
        {
            if (!compilation.ContainsSyntaxTree(body.SyntaxTree))
            {
                continue;
            }

            var semanticModel = compilation.GetCachedSemanticModel(body.SyntaxTree);
            if (RemovedCalls.DescendantNodesThatRun(body, semanticModel)
                .AsStackEnumerable()
                .Any((semanticModel, query), static (node, state) =>
                    node is InvocationExpressionSyntax invocation &&
                    state.semanticModel.GetSymbol(invocation) is IMethodSymbol callee &&
                    state.query.Matches(SyncIntrinsicClassifier.Classify(callee))))
            {
                return true;
            }
        }

        var nextCaller = new ReachStep(method, caller);
        foreach (var site in DirectCallees.Of(bodies, TypeArgumentMap.Of(method), compilation))
        {
            var callee = ToCanonicalDefinitionPart(site.Callee.GetConstructedReducedFrom() ?? site.Callee);
            if (IsFollowedCallee(callee, compilation) &&
                ReachesBarrier(callee, query, compilation, nextCaller, visited))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ReachStep(IMethodSymbol function, ReachStep? caller)
    {
        public IMethodSymbol Function { get; } = function;

        public ReachStep? Caller { get; } = caller;
    }

    private static bool StopsPolymorphicRecursion(IMethodSymbol function, ReachStep? caller, Compilation compilation)
    {
        if (caller is null)
        {
            return false;
        }

        var definition = GenericExpansion.GetCanonicalDefinition(function);
        if (!GenericExpansion.GetExpandingCallees(GenericExpansion.GetCanonicalDefinition(caller.Function), compilation)
                .AsStackEnumerable()
                .Any(definition, static (callee, own) => callee.IsEqualTo(own)))
        {
            return false;
        }

        for (var enclosing = caller; enclosing != null; enclosing = enclosing.Caller)
        {
            if (GenericExpansion.GetCanonicalDefinition(enclosing.Function).IsEqualTo(definition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CallsReach(IMethodSymbol method, IMethodSymbol target, Compilation compilation,
        HashSet<IMethodSymbol> visited)
    {
        var definition = method.PartialDefinitionPart ?? method;
        var targetDefinition = target.PartialDefinitionPart ?? target;
        // A device function of another assembly cannot call back into this one, which it does not reference
        if (!visited.Add(definition) || !TryGetDeviceFunctionBody(definition, compilation, out var declaration))
        {
            return false;
        }

        foreach (var callee in GetDeviceFunctionCallees(declaration, compilation))
        {
            if (SymbolEqualityComparer.Default.Equals(callee, targetDefinition) ||
                CallsReach(callee, targetDefinition, compilation, visited))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The definitions of the <c>[GpuDeviceFunction]</c> methods that <paramref name="body"/> calls, outside the
    /// calls that C# removes (<see cref="RemovedCalls"/>), in call order, with repeats: those with a body in the
    /// compilation, and those of another assembly that recorded them (<see cref="DeviceExportTable"/>).
    /// Does not follow struct members: struct members have no state-machine frame.
    /// </summary>
    internal static IEnumerable<IMethodSymbol> GetDeviceFunctionCallees(SyntaxNode body, Compilation compilation)
    {
        var semanticModel = compilation.GetCachedSemanticModel(body.SyntaxTree);
        foreach (var node in RemovedCalls.DescendantNodesThatRun(body, semanticModel))
        {
            if (node is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol callee &&
                IsDeviceFunctionCallee(callee, compilation))
            {
                var definition = DeviceFunctionEffects.GetDefinition(callee);
                yield return definition.PartialDefinitionPart ?? definition;
            }
        }
    }

    // A [GpuDeviceFunction] with a body in the compilation, or one of another assembly that recorded it
    private static bool IsDeviceFunctionCallee(IMethodSymbol callee, Compilation compilation)
    {
        var definition = DeviceFunctionEffects.GetDefinition(callee);
        var declarationDefinition = definition.PartialDefinitionPart ?? definition;
        return TryGetDeviceFunctionBody(declarationDefinition, compilation, out _) ||
               DeviceExportTable.TryFindFunction(definition, compilation, out _);
    }

    private static bool IsFollowedCallee(IMethodSymbol callee, Compilation compilation) =>
        callee.MethodKind != MethodKind.LocalFunction &&
        (IsDeviceFunctionCallee(callee, compilation) ||
         (IsEmittedStructMember(callee) &&
          DeviceFunctionBodies.TryGet(DeviceFunctionEffects.GetDefinition(callee), compilation, out _)));

    // Local functions are excluded explicitly even when declared inside an emitted struct: their bodies live inside
    // their enclosing member's bodies and are never standalone device functions
    private static bool IsEmittedStructMember(IMethodSymbol method) =>
        method.MethodKind != MethodKind.LocalFunction &&
        !method.HasAttribute(KnownAttributes.GpuKernel) &&
        method.ContainingType is { } type &&
        EmittedUserTypes.IsEmittedUserType(type);

    private static bool TryGetReachableBodies(IMethodSymbol method, bool alsoKernels, Compilation compilation,
        out ImmutableArray<SyntaxNode> bodies)
    {
        var definition = DeviceFunctionEffects.GetDefinition(method);
        var declarationDefinition = definition.PartialDefinitionPart ?? definition;
        var hasDeclaration = alsoKernels
            ? TryGetMethodBody(declarationDefinition, compilation, out var declaration)
            : TryGetDeviceFunctionBody(declarationDefinition, compilation, out declaration);
        if (hasDeclaration && declaration != null && declaration.GetBodyNode() is { } body)
        {
            bodies = ImmutableArray.Create<SyntaxNode>(body);
            return true;
        }

        // Implicit parameterless struct constructors (IsDefaultStructConstructor) zero-initialize and do not execute
        // instance field/property initializers; DeviceFunctionBodies.TryGet returns false for them
        if (IsEmittedStructMember(method) &&
            DeviceFunctionBodies.TryGet(definition, compilation, out bodies))
        {
            return true;
        }

        bodies = default;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a <c>[GpuDeviceFunction]</c> or <c>[GpuKernel]</c> declared with a body in
    /// <paramref name="compilation"/>. A method of another assembly has no body here, also when an IDE gives its
    /// declaration through a referenced compilation.
    /// </summary>
    internal static bool TryGetMethodBody(IMethodSymbol method, Compilation compilation,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration) =>
        TryGetBody(method, IsGpuMethod(method), compilation, out declaration);

    /// <summary>
    /// Whether <paramref name="method"/> is a <c>[GpuDeviceFunction]</c> declared with a body in
    /// <paramref name="compilation"/>. A function of another assembly has no body here, also when an IDE gives its
    /// declaration through a referenced compilation.
    /// </summary>
    internal static bool TryGetDeviceFunctionBody(IMethodSymbol method, Compilation compilation,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration) =>
        TryGetBody(method, HasDeviceFunctionAttribute(method), compilation, out declaration);

    private static bool HasDeviceFunctionAttribute(IMethodSymbol method)
    {
        var original = (method.ReducedFrom ?? method).OriginalDefinition;
        return original.HasAttribute(KnownAttributes.GpuDeviceFunction) ||
               (original.PartialImplementationPart is { } implementation &&
                implementation.HasAttribute(KnownAttributes.GpuDeviceFunction)) ||
               (original.PartialDefinitionPart is { } definition &&
                definition.HasAttribute(KnownAttributes.GpuDeviceFunction));
    }

    private static bool IsGpuMethod(IMethodSymbol method)
    {
        var original = (method.ReducedFrom ?? method).OriginalDefinition;
        return GpuExecutionSpace.IsGpuFunction(original) ||
               (original.PartialImplementationPart is { } implementation &&
                GpuExecutionSpace.IsGpuFunction(implementation)) ||
               (original.PartialDefinitionPart is { } definition &&
                GpuExecutionSpace.IsGpuFunction(definition));
    }

    private static bool TryGetBody(IMethodSymbol method, bool hasAttribute, Compilation compilation,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration)
    {
        if (hasAttribute &&
            method.TryGetDeclaringSyntax(out declaration) &&
            compilation.ContainsSyntaxTree(declaration.SyntaxTree) &&
            declaration.HasBody())
        {
            return true;
        }

        declaration = null;
        return false;
    }

    // Which barriers a query looks for, what it takes from the export record of a device function of another assembly,
    // and its answers per compilation. The queries of the splitting, block and grid barriers ask about a device
    // function only; the others also about a kernel
    private sealed class BarrierQuery(
        bool alsoKernels,
        Func<SyncIntrinsicInfo, bool> matches,
        Func<DeviceExportRecord.FunctionTraits, bool, bool> imported)
    {
        public bool AlsoKernels { get; } = alsoKernels;

        public Func<SyncIntrinsicInfo, bool> Matches { get; } = matches;

        public Func<DeviceExportRecord.FunctionTraits, bool, bool> Imported { get; } = imported;

        public PerCompilationSymbolCache<IMethodSymbol, bool> Answers { get; } = new();
    }
}
