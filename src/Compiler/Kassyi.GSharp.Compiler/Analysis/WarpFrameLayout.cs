namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Where the visit counts of the warp collectives of a kernel or device function, and of the device functions it
/// calls, lie in the counts of a GPU thread on the CPU stack models: the static call path of a frame is its offset, and
/// a count adds up over every call through the same path, as the frames of the state machine count.
/// </summary>
/// <remarks>
/// <para>
/// A frame of the method F takes <see cref="Size"/> = 1 + <see cref="CollectiveCount"/> + the sizes of the frames of
/// its calls. With its frame at offset b, the count of its collective site s is at b + 1 + s, and the frame of its call
/// c_i at b + 1 + <see cref="CollectiveCount"/> + the sizes of the frames of c_0 .. c_(i-1). The collectives
/// (<see cref="SyncIntrinsicKind.WarpShuffle"/>, <c>Warp.ActiveMask()</c> included) and the calls of device functions
/// that reach one (<see cref="SuspensionAnalysis.ReachesWarpShuffle"/>) are numbered in document order, each call site
/// on its own; calls that C# removes do not count (<see cref="RemovedCalls"/>). A kernel's frame is at 0.
/// </para>
/// <para>
/// [WHY] The first slot of a frame is its own and holds no count: without it, a function without collectives of its
/// own and its first callee would share an offset, and so a static path, and the reconvergence regions of the two,
/// matched by path and site, would meet. Every frame taking at least one slot in preorder makes the offset of a path
/// unique. The state machine reaches the same equivalence through one frame per call site, embedded in its caller's
/// frame and never cleared on return.
/// </para>
/// <para>
/// A site belongs to the syntax, not to the place it is written out: the copies of a <c>Gpu.ForUnroll</c> body share
/// the sites and frames of the one body, as the state machine counts them in one loop. Collectives inside a lambda or a
/// local function count as sites of the enclosing method.
/// </para>
/// </remarks>
internal sealed class WarpFrameLayout
{
    internal const int MaxSize = 4096;

    private static readonly PerCompilationSymbolCache<IMethodSymbol, WarpFrameLayout?> s_layouts = new();

    private readonly Dictionary<TextSpan, int> _sites;
    private readonly Dictionary<TextSpan, int> _callOffsets;

    private WarpFrameLayout(int size, Dictionary<TextSpan, int> sites, Dictionary<TextSpan, int> callOffsets)
    {
        Size = size;
        _sites = sites;
        _callOffsets = callOffsets;
    }

    /// <summary>
    /// The slots of the frame: 1 + <see cref="CollectiveCount"/> + the sizes of the frames of the calls.
    /// </summary>
    public int Size { get; }

    /// <summary>The number of warp collectives in the body of the method.</summary>
    public int CollectiveCount => _sites.Count;

    /// <summary>
    /// The site of the warp collective <paramref name="call"/>, a node of the method's declaration or of a lowered copy
    /// of it that keeps its original span.
    /// </summary>
    /// <param name="call">The call of the collective.</param>
    /// <param name="treeOrdinal">
    /// The ordinal of the tree of the method, to read the original span of a lowered node.
    /// </param>
    /// <param name="site">The site, from 0 in document order.</param>
    public bool TryGetSite(InvocationExpressionSyntax call, int treeOrdinal, out int site)
    {
        site = 0;
        return TryGetKey(call, treeOrdinal, out var key) && _sites.TryGetValue(key, out site);
    }

    /// <summary>
    /// The offset of the frame of the device function that <paramref name="call"/> calls, from the offset of this
    /// method's frame.
    /// </summary>
    /// <param name="call">The call of a device function that reaches a warp collective.</param>
    /// <param name="treeOrdinal">
    /// The ordinal of the tree of the method, to read the original span of a lowered node.
    /// </param>
    /// <param name="offset">The offset from the start of this method's frame.</param>
    public bool TryGetCallOffset(InvocationExpressionSyntax call, int treeOrdinal, out int offset)
    {
        offset = 0;
        return TryGetKey(call, treeOrdinal, out var key) && _callOffsets.TryGetValue(key, out offset);
    }

    /// <summary>
    /// The layout of the frame of <paramref name="method"/>; <see langword="false"/> when a collective or a call cannot
    /// be placed: the method or a device function it calls that reaches a collective has no body in
    /// <paramref name="compilation"/> (another assembly), a call of a kernel reaches one, a call reaches a function
    /// again (recursion, GSH0061), or the counts would not fit in an <see cref="int"/>.
    /// </summary>
    /// <remarks>
    /// The size doubles with every level of a function that calls the next one twice, as the nested frames of the
    /// state machine do; a layout whose size or an offset would pass <see cref="int.MaxValue"/> fails instead of
    /// wrapping around. What a launch does with a large but representable size is the runtime's business.
    /// </remarks>
    public static bool TryGet(IMethodSymbol method, Compilation compilation,
        [NotNullWhen(true)] out WarpFrameLayout? layout)
    {
        var onPath = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        layout = Get(method.OriginalDefinition, compilation, onPath);
        return layout is not null;
    }

    // [WHY] A node of a lowered tree finds its site by the span of the source it stands for. A node that carries an
    // original span only for another tree or an inlined copy (another method's body) has no site here; its own span
    // would be a position in the lowered tree, which could match an unrelated call
    private static bool TryGetKey(InvocationExpressionSyntax call, int treeOrdinal, out TextSpan key)
    {
        if (!call.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
        {
            key = call.Span;
            return true;
        }

        if (IrSyntaxIndex.OriginalSpanOf(call, treeOrdinal) is { } span)
        {
            key = span;
            return true;
        }

        key = default;
        return false;
    }

    // [WHY] onPath holds the methods whose layout is being built. A call that reaches one of them again is recursion:
    // its frame would contain itself. Only the methods on that cycle and their callers fail, so a cached failure is
    // never wrong for another caller
    private static WarpFrameLayout? Get(IMethodSymbol method, Compilation compilation, HashSet<IMethodSymbol> onPath)
    {
        var layouts = s_layouts.For(compilation);
        if (layouts.TryGetValue(method, out var cached))
        {
            return cached;
        }

        if (!onPath.Add(method))
        {
            return null;
        }

        var layout = Build(method, compilation, onPath);
        onPath.Remove(method);
        layouts.TryAdd(method, layout);
        return layout;
    }

    private static WarpFrameLayout? Build(IMethodSymbol method, Compilation compilation, HashSet<IMethodSymbol> onPath)
    {
        if (!SuspensionAnalysis.TryGetMethodBody(method, compilation, out var declaration))
        {
            return null;
        }

        var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        var sites = new Dictionary<TextSpan, int>();
        var calls = ImmutableArray.CreateBuilder<(TextSpan Span, int Size)>();
        foreach (var node in RemovedCalls.DescendantNodesThatRun(declaration, model))
        {
            if (node is not InvocationExpressionSyntax call || model.GetSymbol(call) is not IMethodSymbol callee)
            {
                continue;
            }

            if (SyncIntrinsicClassifier.Classify(callee).Kind == SyncIntrinsicKind.WarpShuffle)
            {
                sites.Add(call.Span, sites.Count);
            }
            else if (SuspensionAnalysis.ReachesWarpShuffle(callee, compilation))
            {
                // Only a device function gets a frame. A kernel called like a function has a body here (Build takes
                // kernels as roots), so it would get one without this check; a device function of another assembly
                // has no body and fails in Build anyway
                if (!SuspensionAnalysis.TryGetDeviceFunctionBody(callee.OriginalDefinition, compilation, out _) ||
                    Get(callee.OriginalDefinition, compilation, onPath) is not { } frame)
                {
                    return null;
                }

                calls.Add((call.Span, frame.Size));
            }
        }

        var callOffsets = new Dictionary<TextSpan, int>(calls.Count);
        long offset = 1 + sites.Count;
        foreach (var (span, size) in calls)
        {
            callOffsets.Add(span, (int)offset);
            offset += size;
            if (offset > int.MaxValue)
            {
                return null;
            }
        }

        return new WarpFrameLayout((int)offset, sites, callOffsets);
    }
}
