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
/// the Core intrinsics.
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
            found = Find(definition, compilation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)) ?? "";
            answers.TryAdd(definition, found);
        }

        return found.Length == 0 ? null : found;
    }

    private static string? Find(IMethodSymbol method, Compilation compilation, HashSet<IMethodSymbol> visited)
    {
        if (!visited.Add(method))
        {
            return null;
        }

        if (!method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            !declaration.HasBody())
        {
            return FindImported(method, compilation);
        }

        var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        return RemovedCalls.DescendantNodesThatRun(declaration, model)
            .AsStackEnumerable()
            .Select(
                (model, compilation, visited),
                static (node, state) => FindInNode(node, state.model) ??
                                        FindInCallee(node, state.model, state.compilation, state.visited)
            ).FirstOrDefault(static found => found is not null);
    }

    // The name of each call and method group of a device function (F, T.F, x.F, F<int>), walked where it is named
    private static string? FindInCallee(SyntaxNode node, SemanticModel model, Compilation compilation,
        HashSet<IMethodSymbol> visited) =>
        node is SimpleNameSyntax &&
        model.GetSymbolInfo(node).Symbol is IMethodSymbol callee &&
        ManagedVersionAnalysis.GetDefinition(callee) is var definition &&
        definition.HasAttribute(KnownAttributes.GpuDeviceFunction)
            ? Find(definition, compilation, visited)
            : null;

    // An intrinsic that takes the other threads, a shared memory declaration or a managed-only rewrite
    private static string? FindInNode(SyntaxNode node, SemanticModel model)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation when model.GetSymbol(invocation) is IMethodSymbol callee:
                if (NeedsOtherThreads(SyncIntrinsicClassifier.Classify(callee)))
                {
                    return $"{callee.ContainingType.Name}.{callee.Name}(){OneThreadAtATime}";
                }

                if (ManagedRewriteClassifier.IsSequentialSum(invocation, model))
                {
                    return "a float or double StackLinq Sum, which only the generated code sums in order";
                }

                return ManagedRewriteClassifier.IsInlinePtx(invocation, model)
                    ? "Gpu.InlinePtx, which has no C# implementation"
                    : null;
            case AssignmentExpressionSyntax assignment
                when ManagedRewriteClassifier.TryGetOperatorMacro(assignment, model, out _):
                return "an operator macro ([GpuOperatorMacro]), which only the generated code expands";
            case BaseObjectCreationExpressionSyntax creation when model.GetTypeSymbol(creation).IsSharedMemoryType():
                return "SharedMemory<T>" + OneThreadAtATime;
            default:
                return null;
        }
    }

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

        if (traits.ReachesDynamicSharedMemory)
        {
            return $"{name} of another assembly, which uses SharedMemory<T>{OneThreadAtATime}";
        }

        return traits.ManagedVersion
            ? $"{name} of another assembly, whose C# meaning only the generated code gives"
            : null;
    }
}
