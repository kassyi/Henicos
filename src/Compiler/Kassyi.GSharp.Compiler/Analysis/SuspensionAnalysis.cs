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
    // [WHY] Only the queried function is cached: a depth-first walk that stops at an already visited function would
    // record a wrong 'false' for the functions inside a cycle.
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, bool>> s_suspending = new();

    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, bool>> s_blockBarrier = new();

    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, bool>> s_gridBarrier = new();

    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, bool>> s_warpShuffle = new();

    // Which barriers a query looks for
    private enum BarrierSet
    {
        // Any intrinsic that splits the state machine, also a warp shuffle
        Splitting,

        // Gpu.SyncThreads() and Gpu.SyncGrid(), which every thread of the block must reach
        Block,

        // Gpu.SyncGrid(), which every thread of the grid must reach
        Grid,

        // Warp.Shuffle*Sync and Gpu.Match*Sync
        WarpShuffle
    }

    /// <summary>Whether <paramref name="method"/> is a device function that reaches a phase barrier.</summary>
    public static bool IsSuspendingFunction(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        if (!TryGetDeviceFunctionBody(definition, compilation, out _))
        {
            return DeviceExportTable.TryFindFunction(definition, compilation, out var imported) && imported.Record.Suspends;
        }

        var cache = s_suspending.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default));
        return cache.GetOrAdd(definition,
            m => ReachesBarrier(m, BarrierSet.Splitting, compilation,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)));
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a device function that reaches a block barrier (<c>Gpu.SyncThreads()</c> or
    /// <c>Gpu.SyncGrid()</c>), directly or through other device functions.
    /// </summary>
    /// <remarks>
    /// [WHY] Every thread of the block must reach a block barrier, so a call to such a function must not sit in a branch
    /// (GSH0037). A warp shuffle takes the mask of the threads that reach it, so a call that reaches only shuffles may.
    /// </remarks>
    public static bool ReachesBlockBarrier(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        if (!TryGetDeviceFunctionBody(definition, compilation, out _))
        {
            return DeviceExportTable.TryFindFunction(definition, compilation, out var imported) &&
                   imported.Record.ReachesBlockBarrier;
        }

        var cache = s_blockBarrier.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default));
        return cache.GetOrAdd(definition,
            m => ReachesBarrier(m, BarrierSet.Block, compilation,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)));
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a device function that reaches a grid barrier (<c>Gpu.SyncGrid()</c>),
    /// directly or through other device functions.
    /// </summary>
    /// <remarks>
    /// [WHY] Every thread of the grid must reach a grid barrier, so a call to such a function diverges also on a branch
    /// that varies only between blocks (GSH0083). The export record of a device function of another assembly does not
    /// say which barrier it reaches; such a call is taken as reaching a block barrier only.
    /// </remarks>
    public static bool ReachesGridBarrier(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        if (!TryGetDeviceFunctionBody(definition, compilation, out _))
        {
            return false;
        }

        var cache = s_gridBarrier.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default));
        return cache.GetOrAdd(definition,
            m => ReachesBarrier(m, BarrierSet.Grid, compilation,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)));
    }

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
    public static bool ReachesWarpShuffle(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        if (!TryGetMethodBody(definition, compilation, out _))
        {
            return DeviceExportTable.TryFindFunction(definition, compilation, out var imported) &&
                   imported.Record.Suspends;
        }

        var cache = s_warpShuffle.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default));
        return cache.GetOrAdd(definition,
            m => ReachesBarrier(m, BarrierSet.WarpShuffle, compilation,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)));
    }

    /// <summary>Whether <paramref name="invocation"/>, a node of the tree of <paramref name="semanticModel"/>, calls a
    /// device function that reaches a phase barrier.</summary>
    public static bool IsSuspendingCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel) =>
        semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
        IsSuspendingFunction(method, semanticModel.Compilation);

    /// <summary>
    /// Whether <paramref name="method"/> reaches a phase barrier and calls itself, directly or through other device
    /// functions. Its frame would have to contain itself (GSH0061).
    /// </summary>
    public static bool IsRecursiveSuspendingFunction(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        return IsSuspendingFunction(definition, compilation) &&
               CallsReach(definition, definition, compilation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a device function that the CPU dialects lower into a state machine: one that
    /// reaches a phase barrier and is not recursive. A recursive one is rejected (GSH0061), so no state machine is generated for it.
    /// </summary>
    public static bool RequiresStateMachine(IMethodSymbol method, Compilation compilation) =>
        IsSuspendingFunction(method, compilation) && !IsRecursiveSuspendingFunction(method, compilation);

    /// <summary>The device functions that reach a phase barrier and are called from <paramref name="body"/>, in call order.</summary>
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

    private static bool ReachesBarrier(IMethodSymbol method, BarrierSet barriers, Compilation compilation,
        HashSet<IMethodSymbol> visited)
    {
        if (!visited.Add(method))
        {
            return false;
        }

        if (!TryGetMethodBody(method, compilation, out var declaration))
        {
            // A device function of another assembly: what its assembly recorded
            return DeviceExportTable.TryFindFunction(method, compilation, out var imported) &&
                   barriers switch
                   {
                       BarrierSet.Splitting or BarrierSet.WarpShuffle => imported.Record.Suspends,
                       BarrierSet.Block => imported.Record.ReachesBlockBarrier,
                       _ => false
                   };
        }

        var semanticModel = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        var callsBarrier = declaration.DescendantNodes()
            .AsStackEnumerable()
            .Any((semanticModel, barriers), static (node, state) =>
                node is InvocationExpressionSyntax invocation &&
                state.semanticModel.GetSymbol(invocation) is IMethodSymbol callee &&
                IsBarrier(SyncIntrinsicClassifier.Classify(callee), state.barriers)
            );
        if (callsBarrier)
        {
            return true;
        }

        foreach (var callee in GetDeviceFunctionCallees(declaration, compilation))
        {
            if (ReachesBarrier(callee, barriers, compilation, visited))
            {
                return true;
            }
        }

        return false;
    }

    // A block barrier is one that every thread of the block must reach (SyncThreads, SyncGrid), a grid barrier one that
    // every thread of the grid must reach (SyncGrid); any other barrier is one that splits the state machine (also a warp
    // shuffle)
    private static bool IsBarrier(SyncIntrinsicInfo intrinsic, BarrierSet barriers) =>
        barriers switch
        {
            BarrierSet.Block => intrinsic.Kind is SyncIntrinsicKind.BlockBarrier or SyncIntrinsicKind.GridBarrier,
            BarrierSet.Grid => intrinsic.Kind == SyncIntrinsicKind.GridBarrier,
            BarrierSet.WarpShuffle => intrinsic.Kind == SyncIntrinsicKind.WarpShuffle,
            _ => intrinsic.SplitsStateMachine
        };

    private static bool CallsReach(IMethodSymbol method, IMethodSymbol target, Compilation compilation,
        HashSet<IMethodSymbol> visited)
    {
        // A device function of another assembly cannot call back into this one, which it does not reference
        if (!visited.Add(method) || !TryGetDeviceFunctionBody(method, compilation, out var declaration))
        {
            return false;
        }

        foreach (var callee in GetDeviceFunctionCallees(declaration, compilation))
        {
            if (SymbolEqualityComparer.Default.Equals(callee, target) ||
                CallsReach(callee, target, compilation, visited))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The definitions of the <c>[GpuDeviceFunction]</c> methods that <paramref name="body"/> calls, in call order, with
    /// repeats: those with a body in the compilation, and those of another assembly that recorded them
    /// (<see cref="DeviceExportTable"/>).
    /// </summary>
    internal static IEnumerable<IMethodSymbol> GetDeviceFunctionCallees(SyntaxNode body, Compilation compilation)
    {
        var semanticModel = compilation.GetSemanticModel(body.SyntaxTree);
        foreach (var node in body.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol callee &&
                (TryGetDeviceFunctionBody(callee.OriginalDefinition, compilation, out _) ||
                 DeviceExportTable.TryFindFunction(callee, compilation, out _)))
            {
                yield return callee.OriginalDefinition;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a <c>[GpuDeviceFunction]</c> declared with a body in
    /// <paramref name="compilation"/>. A function of another assembly has no body here, also when an IDE gives its
    /// declaration through a referenced compilation.
    /// </summary>
    private static bool TryGetMethodBody(IMethodSymbol method, Compilation compilation,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration)
    {
        if ((method.HasAttribute(KnownAttributes.GpuDeviceFunction) || method.HasAttribute(KnownAttributes.GpuKernel)) &&
            method.TryGetDeclaringSyntax(out declaration) &&
            compilation.ContainsSyntaxTree(declaration.SyntaxTree) &&
            (declaration.Body != null || declaration.ExpressionBody != null))
        {
            return true;
        }

        declaration = null;
        return false;
    }

    internal static bool TryGetDeviceFunctionBody(IMethodSymbol method, Compilation compilation,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration)
    {
        if (method.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
            method.TryGetDeclaringSyntax(out declaration) &&
            compilation.ContainsSyntaxTree(declaration.SyntaxTree) &&
            (declaration.Body != null || declaration.ExpressionBody != null))
        {
            return true;
        }

        declaration = null;
        return false;
    }
}
