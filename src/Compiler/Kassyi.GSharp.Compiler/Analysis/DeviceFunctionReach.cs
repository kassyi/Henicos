namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The C++ names of the <c>[GpuDeviceFunction]</c> methods that a kernel reaches, directly or through other device
/// functions, for the spill warnings that ptxas reports per function (<see cref="SpillSuppressionPlan"/>).
/// </summary>
/// <remarks>
/// The calls are those of <see cref="SuspensionAnalysis.GetDeviceFunctionCallees"/>. A device function of another
/// assembly counts with its name, but its own callees are not known here. A function reached in a way that list does
/// not see (a method group called through a delegate) is left out, so its spills are reported, never hidden.
/// </remarks>
internal static class DeviceFunctionReach
{
    // [WHY] The direct callees of each function are found once per compilation; the walk of each kernel reuses them.
    private static readonly PerCompilationSymbolCache<IMethodSymbol, ImmutableArray<IMethodSymbol>> s_callees = new();

    /// <summary>
    /// The names of the device functions that <paramref name="body"/> reaches, sorted, without repeats.
    /// </summary>
    public static EquatableArray<string> GetReachedFunctionNames(SyntaxNode body, Compilation compilation)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IMethodSymbol>(SuspensionAnalysis.GetDeviceFunctionCallees(body, compilation));
        while (pending.Count > 0)
        {
            var function = pending.Pop();
            if (!visited.Add(function))
            {
                continue;
            }

            names.Add(GlobalCppNames.GetFunctionName(function));
            foreach (var callee in GetDirectCallees(function, compilation))
            {
                pending.Push(callee);
            }
        }

        return names.ToImmutableArray().AsEquatableArray();
    }

    private static ImmutableArray<IMethodSymbol> GetDirectCallees(IMethodSymbol function, Compilation compilation) =>
        s_callees.For(compilation).GetOrAdd(function, f =>
            SuspensionAnalysis.TryGetDeviceFunctionBody(f, compilation, out var declaration)
                ? SuspensionAnalysis.GetDeviceFunctionCallees(declaration, compilation).ToImmutableArray()
                : ImmutableArray<IMethodSymbol>.Empty);
}
