namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Finds what keeps a kernel from running as C# one GPU thread at a time (<c>KernelExecution.CSharp</c> of the
/// runtime): an operation that waits for or reads the other threads of the block, warp or grid, block-shared memory, or
/// code whose C# meaning only the managed path rewrites. The generated launcher of such a kernel throws in that mode
/// instead of running it.
/// </summary>
/// <remarks>
/// <para>
/// The walk covers the kernel and every <c>[GpuDeviceFunction]</c> it calls or names as a method group, also inside
/// lambdas, outside the calls that C# removes. A device function of another assembly is judged by its export record:
/// one that suspends reaches a barrier or a warp collective, and one with a managed version holds a managed-only
/// rewrite.
/// What the walk cannot see (a delegate parameter, a helper that is not a device function) is caught at run time by
/// the Core intrinsics. The same walk tells whether a device function reaches <c>SharedMemory&lt;T&gt;</c>, which its
/// export record carries for the assemblies that call it (<see cref="ReachesSharedMemory"/>).
/// </para>
/// <para>
/// [WHY] Only the queried kernel is cached, as in <see cref="SuspensionAnalysis"/>: a depth-first walk that stops at an
/// already visited function would record a wrong answer for the functions inside a cycle.
/// </para>
/// </remarks>
internal static class CSharpExecutionAnalysis
{
    // The end of the reason of an operation that needs the other threads
    private const string OneThreadAtATime = ", and the C# execution runs one GPU thread at a time";

    private static readonly PerCompilationSymbolCache<IMethodSymbol, string> s_answers = new();

    private static readonly PerCompilationSymbolCache<IMethodSymbol, bool> s_sharedMemory = new();

    private static readonly PerCompilationSymbolCache<IMethodSymbol, bool> s_generatedMeaning = new();

    // What makes a kernel unable to run one thread at a time
    private static readonly Query s_obstacle = new(FindInNode, FindImported);

    // A SharedMemory<T> declaration, static or dynamic
    private static readonly Query s_sharedMemoryQuery = new(FindSharedMemory, FindImportedSharedMemory);

    // Code whose C# meaning only the generated code gives
    private static readonly Query s_generatedMeaningQuery = new(FindGeneratedMeaning, FindImportedGeneratedMeaning);

    /// <summary>
    /// The first construct that <paramref name="kernel"/> reaches and that cannot run one GPU thread at a time, as the
    /// user writes it, with why (<c>Gpu.SyncThreads(), and the C# execution runs one GPU thread at a time</c>), or
    /// <see langword="null"/> when the kernel can run as C#.
    /// </summary>
    public static string? FindObstacle(IMethodSymbol kernel, Compilation compilation)
    {
        var definition = kernel.OriginalDefinition;
        var answers = s_answers.For(compilation);
        if (!answers.TryGetValue(definition, out var found))
        {
            // "" for none: the cache keeps no null
            found = Find(definition, s_obstacle, compilation, NewVisited()) ?? "";
            answers.TryAdd(definition, found);
        }

        return found.Length == 0 ? null : found;
    }

    /// <summary>
    /// Whether <paramref name="method"/> (a kernel or a device function) reaches a <c>SharedMemory&lt;T&gt;</c>
    /// declaration, static or dynamic, directly or through the device functions it calls or names.
    /// </summary>
    public static bool ReachesSharedMemory(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        var answers = s_sharedMemory.For(compilation);
        if (!answers.TryGetValue(definition, out var reaches))
        {
            reaches = Find(definition, s_sharedMemoryQuery, compilation, NewVisited()) is not null;
            answers.TryAdd(definition, reaches);
        }

        return reaches;
    }

    /// <summary>
    /// Whether <paramref name="method"/> (a kernel or a device function) reaches code whose C# meaning only the
    /// generated code gives (an operator macro, a float or double StackLinq Sum, Gpu.InlinePtx), directly or through
    /// the device functions it calls or names. A managed version can exist for another reason as well: an int or long
    /// division that may divide the minimum by -1 (<c>ManagedDivision</c>), whose C# meaning the C# execution keeps.
    /// </summary>
    public static bool ReachesGeneratedMeaning(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        var answers = s_generatedMeaning.For(compilation);
        if (!answers.TryGetValue(definition, out var reaches))
        {
            reaches = Find(definition, s_generatedMeaningQuery, compilation, NewVisited()) is not null;
            answers.TryAdd(definition, reaches);
        }

        return reaches;
    }

    private static HashSet<IMethodSymbol> NewVisited() => new(SymbolEqualityComparer.Default);

    private static string? Find(IMethodSymbol method, Query query, Compilation compilation,
        HashSet<IMethodSymbol> visited)
    {
        if (!visited.Add(method))
        {
            return null;
        }

        if (!method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            !declaration.HasBody())
        {
            return query.Imported(method, compilation);
        }

        var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        return RemovedCalls.DescendantNodesThatRun(declaration, model)
            .AsStackEnumerable()
            .Select(
                (model, query, compilation, visited),
                static (node, state) => state.query.InNode(node, state.model) ??
                                        FindInCallee(node, state.model, state.query, state.compilation, state.visited)
            ).FirstOrDefault(static found => found is not null);
    }

    // The name of each call and method group of a device function (F, T.F, x.F, F<int>), walked where it is named
    private static string? FindInCallee(SyntaxNode node, SemanticModel model, Query query, Compilation compilation,
        HashSet<IMethodSymbol> visited) =>
        node is SimpleNameSyntax &&
        model.GetSymbolInfo(node).Symbol is IMethodSymbol callee &&
        ManagedVersionAnalysis.GetDefinition(callee) is var definition &&
        definition.HasAttribute(KnownAttributes.GpuDeviceFunction)
            ? Find(definition, query, compilation, visited)
            : null;

    // An intrinsic that takes the other threads, a shared memory declaration or a managed-only rewrite
    private static string? FindInNode(SyntaxNode node, SemanticModel model)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation when model.GetSymbol(invocation) is IMethodSymbol callee:
                return NeedsOtherThreads(SyncIntrinsicClassifier.Classify(callee))
                    ? $"{callee.ContainingType.Name}.{callee.Name}(){OneThreadAtATime}"
                    : FindGeneratedMeaning(node, model);
            case AssignmentExpressionSyntax:
                return FindGeneratedMeaning(node, model);
            default:
                return FindSharedMemory(node, model) is null ? null : "SharedMemory<T>" + OneThreadAtATime;
        }
    }

    // A managed-only rewrite whose C# meaning only the generated code gives. [WHY] Not the division that may divide the
    // minimum by -1: the C# execution runs C#, where .NET throws OverflowException, and refusing every kernel that
    // divides would leave the mode for few kernels
    private static string? FindGeneratedMeaning(SyntaxNode node, SemanticModel model) => node switch
    {
        InvocationExpressionSyntax invocation when ManagedRewriteClassifier.IsSequentialSum(invocation, model) =>
            "a float or double StackLinq Sum, which only the generated code sums in order",
        InvocationExpressionSyntax invocation when ManagedRewriteClassifier.IsInlinePtx(invocation, model) =>
            "Gpu.InlinePtx, which has no C# implementation",
        AssignmentExpressionSyntax assignment
            when ManagedRewriteClassifier.TryGetOperatorMacro(assignment, model, out _) =>
            "an operator macro ([GpuOperatorMacro]), which only the generated code expands",
        _ => null
    };

    private static string? FindSharedMemory(SyntaxNode node, SemanticModel model) =>
        node is BaseObjectCreationExpressionSyntax creation && model.GetTypeSymbol(creation).IsSharedMemoryType()
            ? "SharedMemory<T>"
            : null;

    // A barrier, a warp shuffle, vote, match or collective, Warp.ActiveMask() or an mbarrier
    private static bool NeedsOtherThreads(SyncIntrinsicInfo info) =>
        info.Kind is SyncIntrinsicKind.BlockBarrier
            or SyncIntrinsicKind.GridBarrier
            or SyncIntrinsicKind.WarpShuffle
            or SyncIntrinsicKind.WarpCollective
            or SyncIntrinsicKind.AsyncBarrier ||
        info.Shuffle == WarpShuffleKind.ActiveMask;

    // A device function of another assembly: what its assembly recorded
    private static string? FindImported(IMethodSymbol method, Compilation compilation)
    {
        if (!DeviceExportTable.TryFindFunction(method, compilation, out var imported))
        {
            return null;
        }

        var traits = imported.Record.Traits;
        var name = $"{method.ContainingType.Name}.{method.Name}()";
        if (traits.Suspends || traits.ReachesBlockBarrier || traits.ReachesArrivalBarrier)
        {
            return $"{name} of another assembly, which reaches a barrier or a warp collective{OneThreadAtATime}";
        }

        if (traits.ReachesSharedMemory || traits.ReachesDynamicSharedMemory)
        {
            return $"{name} of another assembly, which uses SharedMemory<T>{OneThreadAtATime}";
        }

        return traits.ReachesGeneratedMeaning
            ? $"{name} of another assembly, whose C# meaning only the generated code gives"
            : null;
    }

    private static string? FindImportedGeneratedMeaning(IMethodSymbol method, Compilation compilation) =>
        DeviceExportTable.TryFindFunction(method, compilation, out var imported) &&
        imported.Record.Traits.ReachesGeneratedMeaning
            ? "code whose C# meaning only the generated code gives"
            : null;

    // A device function of another assembly that recorded a SharedMemory<T> declaration; a record written before the
    // static ones were recorded tells only the dynamic ones
    private static string? FindImportedSharedMemory(IMethodSymbol method, Compilation compilation) =>
        DeviceExportTable.TryFindFunction(method, compilation, out var imported) &&
        (imported.Record.Traits.ReachesSharedMemory || imported.Record.Traits.ReachesDynamicSharedMemory)
            ? "SharedMemory<T>"
            : null;

    // What a walk looks for in a body, and in the export record of a device function of another assembly
    private sealed class Query(
        Func<SyntaxNode, SemanticModel, string?> inNode,
        Func<IMethodSymbol, Compilation, string?> imported)
    {
        public Func<SyntaxNode, SemanticModel, string?> InNode { get; } = inNode;

        public Func<IMethodSymbol, Compilation, string?> Imported { get; } = imported;
    }
}
