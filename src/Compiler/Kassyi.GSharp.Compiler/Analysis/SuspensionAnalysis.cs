namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Finds the <c>[GpuDeviceFunction]</c> methods that reach a phase barrier, directly or through other device functions.
/// </summary>
/// <remarks>
/// On the CPU dialects such a function suspends its caller, so it is lowered into its own state machine and every call
/// to it becomes a suspension point. A barrier is what <see cref="BarrierHelper.IsPhaseBarrier"/> splits on, so a device
/// function suspends exactly when kernel-level lowering would split on the same code.
/// </remarks>
internal static class SuspensionAnalysis
{
    // [WHY] Only the queried function is cached: a depth-first walk that stops at an already visited function would
    // record a wrong 'false' for the functions inside a cycle.
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, bool>> s_suspending = new();

    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, bool>> s_blockBarrier = new();

    /// <summary>Whether <paramref name="method"/> is a device function that reaches a phase barrier.</summary>
    public static bool IsSuspendingFunction(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        if (!TryGetDeviceFunctionBody(definition, out _))
        {
            return false;
        }

        var cache = s_suspending.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default));
        return cache.GetOrAdd(definition,
            m => ReachesBarrier(m, BarrierHelper.IsPhaseBarrier, compilation,
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
        if (!TryGetDeviceFunctionBody(definition, out _))
        {
            return false;
        }

        var cache = s_blockBarrier.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default));
        return cache.GetOrAdd(definition,
            m => ReachesBarrier(m, BarrierHelper.IsBlockBarrier, compilation,
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

    private static bool ReachesBarrier(IMethodSymbol method, Func<InvocationExpressionSyntax, bool> isBarrier,
        Compilation compilation, HashSet<IMethodSymbol> visited)
    {
        if (!visited.Add(method) || !TryGetDeviceFunctionBody(method, out var declaration))
        {
            return false;
        }

        foreach (var node in declaration.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation && isBarrier(invocation))
            {
                return true;
            }
        }

        foreach (var callee in GetDeviceFunctionCallees(declaration, compilation))
        {
            if (ReachesBarrier(callee, isBarrier, compilation, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CallsReach(IMethodSymbol method, IMethodSymbol target, Compilation compilation,
        HashSet<IMethodSymbol> visited)
    {
        if (!visited.Add(method) || !TryGetDeviceFunctionBody(method, out var declaration))
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

    private static IEnumerable<IMethodSymbol> GetDeviceFunctionCallees(SyntaxNode body, Compilation compilation)
    {
        var semanticModel = compilation.GetSemanticModel(body.SyntaxTree);
        foreach (var node in body.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol callee &&
                TryGetDeviceFunctionBody(callee.OriginalDefinition, out _))
            {
                yield return callee.OriginalDefinition;
            }
        }
    }

    private static bool TryGetDeviceFunctionBody(IMethodSymbol method,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration)
    {
        if (method.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
            method.TryGetDeclaringSyntax(out declaration) &&
            (declaration.Body != null || declaration.ExpressionBody != null))
        {
            return true;
        }

        declaration = null;
        return false;
    }
}
