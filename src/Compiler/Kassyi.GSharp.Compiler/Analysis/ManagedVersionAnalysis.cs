namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Finds the <c>[GpuDeviceFunction]</c> methods that need a managed version (#711): a copy of the body, rewritten for the
/// managed path, which the managed bodies G# emits call instead of the user's method.
/// </summary>
/// <remarks>
/// <para>
/// The managed path runs a device function that does not suspend as the user's own C# method, which no generated code
/// is part of. A body with a managed-only semantic rewrite (<see cref="ManagedRewriteClassifier"/>: an operator macro,
/// whose operator throws <c>GpuOnlyException</c>, or a <c>float</c>/<c>double</c> <c>StackLinq</c> <c>Sum</c> call that
/// must sum sequentially) would run wrongly there, and so would a body that calls such a
/// function. Those functions get a managed version; every other one keeps running as the user wrote it. A function
/// that suspends has a state machine, whose body the rewriter already rewrites, so it never needs one.
/// </para>
/// <para>
/// A device function of another assembly has no body in the compilation; its assembly recorded whether it emitted a
/// managed version (<see cref="Models.DeviceExportRecord.ManagedVersion"/>).
/// </para>
/// </remarks>
internal static class ManagedVersionAnalysis
{
    // [WHY] Only the queried function is cached, as in SuspensionAnalysis: a depth-first walk that stops at an already
    // visited function would record a wrong 'false' for the functions inside a cycle.
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, bool>> s_needs = new();

    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, bool>> s_direct = new();

    /// <summary>
    /// Whether <paramref name="method"/> is a device function that needs a managed version: one that does not suspend
    /// and whose body holds a managed-only semantic rewrite, or calls (or names as a method group) a device function
    /// that needs one. For a function of another assembly, whether its assembly emitted one.
    /// </summary>
    public static bool Needs(IMethodSymbol method, Compilation compilation)
    {
        var definition = GetDefinition(method);
        if (!SuspensionAnalysis.TryGetDeviceFunctionBody(definition, compilation, out _))
        {
            return IsImportedWithManagedVersion(definition, compilation);
        }

        var cache = s_needs.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default));
        return cache.GetOrAdd(definition,
            m => Reaches(m, compilation, cache, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)));
    }

    /// <summary>
    /// Whether the managed bodies G# emits call the managed version of <paramref name="method"/> instead of the user's
    /// method: a device function of this compilation that needs one and whose containing types are all
    /// <c>partial</c> (the version is written into them), or one of another assembly that recorded one.
    /// </summary>
    /// <remarks>
    /// GSH0036 requires <c>partial</c> on the class of every device function; the check covers the types that contain
    /// that class, for which nothing is emitted and nothing is routed.
    /// </remarks>
    public static bool HasManagedVersion(IMethodSymbol method, Compilation compilation)
    {
        var definition = GetDefinition(method);
        if (!definition.HasAttribute(KnownAttributes.GpuDeviceFunction))
        {
            return false;
        }

        if (!SuspensionAnalysis.TryGetDeviceFunctionBody(definition, compilation, out _))
        {
            return IsImportedWithManagedVersion(definition, compilation);
        }

        return Needs(definition, compilation) && ManagedStateMachineAvailability.AreContainingTypesPartial(definition);
    }

    /// <summary>
    /// Whether the body of <paramref name="declaration"/> holds a managed-only semantic rewrite itself
    /// (<see cref="ManagedRewriteClassifier.IsSemanticRewrite"/>), not counting the functions it calls.
    /// </summary>
    public static bool HasDirectRewrite(IMethodSymbol method, MethodDeclarationSyntax declaration, Compilation compilation)
    {
        var cache = s_direct.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default));
        var definition = GetDefinition(method);
        if (cache.TryGetValue(definition, out var known))
        {
            return known;
        }

        var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        var found = declaration.DescendantNodes()
            .AsStackEnumerable()
            .Any(model, static (node, m) => ManagedRewriteClassifier.IsSemanticRewrite(node, m));
        return cache.GetOrAdd(definition, found);
    }

    /// <summary>
    /// The definitions of the <c>[GpuDeviceFunction]</c> methods that <paramref name="body"/> calls or names as a method
    /// group, with repeats: a reduced extension call as the extension method, a generic call as the generic definition.
    /// </summary>
    internal static IEnumerable<IMethodSymbol> GetReferencedDeviceFunctions(SyntaxNode body, SemanticModel semanticModel)
    {
        foreach (var node in body.DescendantNodes())
        {
            // The name of each call and method group: F, T.F, x.F, F<int>
            if (node is SimpleNameSyntax &&
                semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol method &&
                GetDefinition(method) is var definition &&
                definition.HasAttribute(KnownAttributes.GpuDeviceFunction))
            {
                yield return definition;
            }
        }
    }

    /// <summary>The method that <paramref name="method"/> refers to: the extension method of a reduced call, as declared.</summary>
    internal static IMethodSymbol GetDefinition(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;

    private static bool Reaches(IMethodSymbol method, Compilation compilation,
        ConcurrentDictionary<IMethodSymbol, bool> cache, HashSet<IMethodSymbol> visited)
    {
        if (!visited.Add(method))
        {
            return false;
        }

        if (!SuspensionAnalysis.TryGetDeviceFunctionBody(method, compilation, out var declaration))
        {
            return IsImportedWithManagedVersion(method, compilation);
        }

        // A state machine is rewritten as a whole; a function that suspends calls no function that does not
        if (SuspensionAnalysis.IsSuspendingFunction(method, compilation))
        {
            return false;
        }

        // A function that was queried itself has its full answer
        if (cache.TryGetValue(method, out var known))
        {
            return known;
        }

        if (HasDirectRewrite(method, declaration, compilation))
        {
            return true;
        }

        var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        foreach (var callee in GetReferencedDeviceFunctions(declaration, model))
        {
            if (Reaches(callee, compilation, cache, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsImportedWithManagedVersion(IMethodSymbol method, Compilation compilation) =>
        DeviceExportTable.TryFindFunction(method, compilation, out var imported) && imported.Record.ManagedVersion;
}
