namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Finds the dynamic <c>SharedMemory&lt;T&gt;</c> declarations (a count that is not a C# constant) that a kernel or a
/// <c>[GpuDeviceFunction]</c> reaches, directly or through other device functions.
/// </summary>
/// <remarks>
/// [WHY] On CUDA every dynamic declaration is one region per block, carved out of the one dynamic shared memory buffer
/// in the order a thread first reaches it (#439). A kernel owns the table of those regions; a device function that
/// reaches a declaration receives the table as a hidden argument. The CPU dialects and the managed output look a region
/// up by its site key instead, so they do not use this analysis.
/// </remarks>
internal static class DynamicSharedMemoryAnalysis
{
    /// <summary>The argument of <c>GSH0003</c> when a dynamic declaration is reached from code that has no table.</summary>
    public const string ReachedWithoutTable =
        "dynamic SharedMemory<T> reached outside a kernel or a static device function";

    /// <summary>
    /// The argument of <c>GSH0003</c> when a device function that reaches a dynamic declaration is named without being
    /// called (a method group), in a kernel as well: the delegate call cannot pass the table to the function.
    /// </summary>
    public const string PassedAsDelegate =
        "device function passed as a delegate (it reaches dynamic SharedMemory<T>, and a delegate call cannot pass " +
        "it the table of dynamic shared memory regions)";

    // [WHY] Only the queried function is cached: a depth-first walk that stops at an already visited function would
    // record a wrong 'false' for the functions inside a cycle (as in SuspensionAnalysis).
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, bool>> s_reaches = new();

    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, ImmutableArray<ulong>>> s_sites = new();

    /// <summary>
    /// Reads the count of a <c>SharedMemory&lt;T&gt;</c> local declarator: the creation, its first argument and the
    /// argument's constant value, which is <see langword="null"/> for a dynamic declaration.
    /// </summary>
    /// <returns><see langword="false"/> when the declarator does not create a <c>SharedMemory&lt;T&gt;</c> with a count.</returns>
    public static bool TryGetCount(VariableDeclaratorSyntax declarator, SemanticModel semanticModel,
        [NotNullWhen(true)] out BaseObjectCreationExpressionSyntax? creation,
        [NotNullWhen(true)] out ExpressionSyntax? count, out int? constantCount)
    {
        constantCount = null;
        count = null;
        creation = declarator.Initializer?.Value as BaseObjectCreationExpressionSyntax;
        if (creation == null ||
            !InitializationSyntaxExtractor.TryGetObjectCreationFirstArgument(creation, out count) ||
            count == null)
        {
            return false;
        }

        var constantValue = semanticModel.GetSafeConstantValue(count);
        constantCount = constantValue.HasValue ? Convert.ToInt32(constantValue.Value ?? 0) : null;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a device function that reaches a dynamic <c>SharedMemory&lt;T&gt;</c>
    /// declaration, in its own body or through the device functions it calls.
    /// </summary>
    public static bool ReachesDynamicSharedMemory(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        if (!SuspensionAnalysis.TryGetDeviceFunctionBody(definition, out _))
        {
            return false;
        }

        var cache = s_reaches.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default));
        return cache.GetOrAdd(definition,
            m => Reaches(m, compilation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)));
    }

    /// <summary>
    /// The site keys (<see cref="SharedMemorySiteKey"/>) of the dynamic declarations that <paramref name="kernel"/>
    /// reaches, in ascending order without duplicates. The position of a key is the slot of its region in the table of the
    /// kernel.
    /// </summary>
    public static ImmutableArray<ulong> GetReachableSites(IMethodSymbol kernel, Compilation compilation)
    {
        var definition = kernel.OriginalDefinition;
        var cache = s_sites.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, ImmutableArray<ulong>>(SymbolEqualityComparer.Default));
        return cache.GetOrAdd(definition, m => CollectReachableSites(m, compilation));
    }

    private static ImmutableArray<ulong> CollectReachableSites(IMethodSymbol kernel, Compilation compilation)
    {
        if (!kernel.TryGetDeclaringSyntax(out MethodDeclarationSyntax? declaration) || !declaration.HasBody())
        {
            return ImmutableArray<ulong>.Empty;
        }

        var sites = new SortedSet<ulong>();
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        CollectSites(declaration, compilation, sites, visited);
        return sites.ToImmutableArray();
    }

    private static void CollectSites(MethodDeclarationSyntax declaration, Compilation compilation, SortedSet<ulong> sites,
        HashSet<IMethodSymbol> visited)
    {
        var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
        declaration.DescendantNodes()
            .AsStackEnumerable()
            .OfType<LocalDeclarationStatementSyntax>()
            .Where(semanticModel, static (local, model) => IsSharedMemoryDeclaration(local, model))
            .SelectMany(static local => local.Declaration.Variables
                .AsStackEnumerable()
            ).Select(semanticModel, static (declarator, model) => GetDynamicCreation(declarator, model))
            .OfType<BaseObjectCreationExpressionSyntax>()
            .Select(semanticModel, static (creation, model) => SharedMemorySiteKey.Compute(model, creation))
            .AddTo(sites);

        foreach (var callee in SuspensionAnalysis.GetDeviceFunctionCallees(declaration, compilation))
        {
            if (visited.Add(callee) && SuspensionAnalysis.TryGetDeviceFunctionBody(callee, out var calleeDeclaration))
            {
                CollectSites(calleeDeclaration, compilation, sites, visited);
            }
        }
    }

    private static bool Reaches(IMethodSymbol method, Compilation compilation, HashSet<IMethodSymbol> visited)
    {
        if (!visited.Add(method) || !SuspensionAnalysis.TryGetDeviceFunctionBody(method, out var declaration))
        {
            return false;
        }

        var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
        var declaresDynamic = declaration.DescendantNodes()
            .AsStackEnumerable()
            .OfType<LocalDeclarationStatementSyntax>()
            .Where(semanticModel, static (local, model) => IsSharedMemoryDeclaration(local, model))
            .Any(semanticModel, static (local, model) => local.Declaration.Variables
                .AsStackEnumerable()
                .Any(model, static (declarator, m) => GetDynamicCreation(declarator, m) != null)
            );
        if (declaresDynamic)
        {
            return true;
        }

        foreach (var callee in SuspensionAnalysis.GetDeviceFunctionCallees(declaration, compilation))
        {
            if (Reaches(callee, compilation, visited))
            {
                return true;
            }
        }

        return false;
    }

    // The local declarations that LocalDeclarationStatementTranspilerRule emits as shared memory
    private static bool IsSharedMemoryDeclaration(LocalDeclarationStatementSyntax local, SemanticModel semanticModel) =>
        semanticModel.GetTypeSymbol(local.Declaration.Type).IsSharedMemoryType();

    // The creation of a dynamic declarator, which CUDA places in the dynamic buffer; null for a fixed-size one
    private static BaseObjectCreationExpressionSyntax? GetDynamicCreation(VariableDeclaratorSyntax declarator,
        SemanticModel semanticModel) =>
        TryGetCount(declarator, semanticModel, out var creation, out _, out var constantCount) && constantCount == null
            ? creation
            : null;
}
