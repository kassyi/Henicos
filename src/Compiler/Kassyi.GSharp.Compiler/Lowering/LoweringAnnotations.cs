namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The syntax annotations that the lowering passes attach to the tree they produce. The emitters
/// (<c>StatementTranspilers</c>, <c>ManagedCSharpTranspiler</c>) and the semantic model lookups read them back, so the
/// writer and every reader refer to these constants.
/// </summary>
public static class LoweringAnnotations
{
    /// <summary>Annotation kind that records the span of the original node a lowered node was derived from.</summary>
    public const string OriginalSpanAnnotationKind = "GSharpOriginalSpan";

    /// <summary>Annotation kind that marks a call to a device function that reaches a barrier (see <see cref="SuspensionAnalysis"/>).</summary>
    public const string SuspendingCallAnnotationKind = "GSharpSuspendingCall";

    /// <summary>
    /// Data of a <see cref="SuspendingCallAnnotationKind"/> annotation on a call whose callee returns a value. The state
    /// machine always stores that value, even when the caller discards it.
    /// </summary>
    public const string ValueReturningCallAnnotationData = "Value";

    /// <summary>
    /// Annotation kind that marks a call to a synchronization intrinsic that splits the state machine (a block or grid
    /// barrier, a warp shuffle). The pipeline puts it on the original tree from the symbol of the call
    /// (<see cref="SyncIntrinsicClassifier"/>), so the lowering passes, which work on trees outside the compilation,
    /// recognize the call however it is spelled (a <c>using</c> alias, <c>using static</c>, the full name; #503). The data
    /// is one of <see cref="BlockBarrierAnnotationData"/>, <see cref="GridBarrierAnnotationData"/> and the
    /// <c>Shuffle*AnnotationData</c> constants.
    /// </summary>
    public const string SyncIntrinsicAnnotationKind = "GSharpSyncIntrinsic";

    /// <summary>Data of a <see cref="SyncIntrinsicAnnotationKind"/> annotation on <c>Gpu.SyncThreads()</c>.</summary>
    public const string BlockBarrierAnnotationData = "Block";

    /// <summary>Data of a <see cref="SyncIntrinsicAnnotationKind"/> annotation on <c>Gpu.SyncGrid()</c>.</summary>
    public const string GridBarrierAnnotationData = "Grid";

    /// <summary>Data of a <see cref="SyncIntrinsicAnnotationKind"/> annotation on <c>Warp.ShuffleSync</c>.</summary>
    public const string ShuffleSyncAnnotationData = "Shuffle:Sync";

    /// <summary>Data of a <see cref="SyncIntrinsicAnnotationKind"/> annotation on <c>Warp.ShuffleDownSync</c>.</summary>
    public const string ShuffleDownAnnotationData = "Shuffle:Down";

    /// <summary>Data of a <see cref="SyncIntrinsicAnnotationKind"/> annotation on <c>Warp.ShuffleUpSync</c>.</summary>
    public const string ShuffleUpAnnotationData = "Shuffle:Up";

    /// <summary>Data of a <see cref="SyncIntrinsicAnnotationKind"/> annotation on <c>Warp.ShuffleXorSync</c>.</summary>
    public const string ShuffleXorAnnotationData = "Shuffle:Xor";

    /// <summary>
    /// Annotation kind that marks a <c>return</c> the state machine inserts to suspend (after storing the next state), as
    /// opposed to a <c>return</c> of the user's code. Emitters leave it as a plain suspension instead of finishing the
    /// thread or completing the device function call.
    /// </summary>
    public const string StateMachineReturnAnnotationKind = "StateMachineReturn";

    /// <summary>
    /// Annotation kind that marks a lowered <c>foreach</c> array/span source receiver
    /// (<c>__gsharp_foreach_src_&lt;id&gt;</c>) so <c>.Length</c> emits as <c>.length</c>.
    /// </summary>
    public const string ForEachArraySourceAnnotationKind = "GSharpForEachArraySource";

    /// <summary>
    /// Annotation kind that marks an expression of a lowered tree that the transpiler writes through the null check of a
    /// C# array read (<c>ArrayNullCheck</c>, #706); its data is the place of the user's expression. Set on the source of
    /// a lowered <c>foreach</c>, which C# checks once at the loop entry.
    /// </summary>
    public const string ArrayNullCheckAnnotationKind = "GSharpArrayNullCheck";

    /// <summary>
    /// Annotation kind that marks a lowered <c>foreach</c> loop index declarator
    /// (<c>__gsharp_foreach_i_&lt;id&gt;</c>) as <see cref="int"/> when hoisted into a context struct.
    /// </summary>
    public const string ForEachIndexAnnotationKind = "GSharpForEachIndex";

    /// <summary>
    /// Annotation kind that marks a synthesized <see cref="BlockSyntax"/> whose C++ scope braces must be preserved by
    /// <c>RoslynToCudaVisitor.VisitBlock</c>.
    /// </summary>
    public const string ScopedBlockAnnotationKind = "GSharpScopedBlock";

    /// <summary>
    /// Annotation kind that marks the declarator of an argument temporary (<c>__gsharp_arg_temp_&lt;n&gt;</c>) of a
    /// suspending call or a warp shuffle. Its hoisted field takes the type the argument converts to (the parameter's), not
    /// the argument's own type, so that the temporary passes wherever the argument did.
    /// </summary>
    public const string ArgumentTempAnnotationKind = "GSharpArgumentTemp";

    /// <summary>
    /// The <see cref="SyncIntrinsicAnnotationKind"/> annotation for a call to <paramref name="intrinsic"/>, or
    /// <see langword="null"/> when the intrinsic does not split the state machine.
    /// </summary>
    internal static SyntaxAnnotation? CreateSyncIntrinsicAnnotation(SyncIntrinsicInfo intrinsic)
    {
        if (!intrinsic.SplitsStateMachine)
        {
            return null;
        }

        var data = intrinsic.Kind switch
        {
            SyncIntrinsicKind.BlockBarrier => BlockBarrierAnnotationData,
            SyncIntrinsicKind.GridBarrier => GridBarrierAnnotationData,
            SyncIntrinsicKind.WarpShuffle => intrinsic.Shuffle switch
            {
                WarpShuffleKind.Sync => ShuffleSyncAnnotationData,
                WarpShuffleKind.Down => ShuffleDownAnnotationData,
                WarpShuffleKind.Up => ShuffleUpAnnotationData,
                WarpShuffleKind.Xor => ShuffleXorAnnotationData,
                _ => null
            },
            _ => null
        };
        return data == null ? null : new SyntaxAnnotation(SyncIntrinsicAnnotationKind, data);
    }

    /// <summary>Formats the <see cref="OriginalSpanAnnotationKind"/> payload for <paramref name="node"/>.</summary>
    public static string FormatOriginalSpanData(SyntaxNode node, int treeOrdinal = -1, int region = 0) =>
        FormatOriginalSpanData(node.SpanStart, node.Span.Length, treeOrdinal, region);

    /// <summary>Formats an <see cref="OriginalSpanAnnotationKind"/> payload from its component integers.</summary>
    public static string FormatOriginalSpanData(int start, int length, int treeOrdinal = -1, int region = 0)
    {
        if (region > 0)
        {
            return $"{start},{length},{(treeOrdinal >= 0 ? treeOrdinal : -1)},{region}";
        }

        return treeOrdinal >= 0
            ? $"{start},{length},{treeOrdinal}"
            : $"{start},{length}";
    }

    /// <summary>
    /// Parses an <see cref="OriginalSpanAnnotationKind"/> payload of 2, 3, or 4 comma-separated integers
    /// (<c>"{start},{length}[,{treeOrdinal}[,{region}]]"</c>).
    /// </summary>
    public static bool TryParseOriginalSpanData(
        string? data,
        out int start,
        out int length,
        out int treeOrdinal,
        out int region)
    {
        start = 0;
        length = 0;
        treeOrdinal = -1;
        region = 0;

        if (data is not { Length: > 0 })
        {
            return false;
        }

        var firstComma = data.IndexOf(',');
        if (firstComma <= 0 || !int.TryParse(data[..firstComma], out start))
        {
            return false;
        }

        var rest = data[(firstComma + 1)..];
        var secondComma = rest.IndexOf(',');
        if (secondComma < 0)
        {
            return int.TryParse(rest, out length);
        }

        if (!int.TryParse(rest[..secondComma], out length))
        {
            return false;
        }

        var afterSecond = rest[(secondComma + 1)..];
        var thirdComma = afterSecond.IndexOf(',');
        if (thirdComma < 0)
        {
            return int.TryParse(afterSecond, out treeOrdinal);
        }

        if (!int.TryParse(afterSecond[..thirdComma], out treeOrdinal) ||
            !int.TryParse(afterSecond[(thirdComma + 1)..], out region))
        {
            return false;
        }

        if (region < 0)
        {
            region = 0;
        }

        return true;
    }

    /// <summary>
    /// Stamps <paramref name="region"/> onto the <see cref="OriginalSpanAnnotationKind"/> annotations of declarations
    /// introduced within <paramref name="copy"/> and of <see cref="SimpleNameSyntax"/> references that bind to those
    /// declarations, leaving references to outer declarations untouched.
    /// </summary>
    public static TNode StampRegion<TNode>(TNode copy, int region, SemanticModel? model) where TNode : SyntaxNode
    {
        if (region <= 0)
        {
            return copy;
        }

        HashSet<ISymbol>? declaredSymbols = null;
        if (model != null)
        {
            declaredSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var node in copy.DescendantNodesAndSelf())
            {
                if (IsDeclarationNode(node) &&
                    node.HasAnnotations(OriginalSpanAnnotationKind) &&
                    model.GetDeclaredSymbol<ISymbol>(node) is { } symbol)
                {
                    declaredSymbols.Add(symbol.OriginalDefinition);
                }
            }
        }

        var stamper = new RegionStamper(region, model, declaredSymbols);
        return stamper.Visit(copy) as TNode ?? copy;
    }

    private static bool IsDeclarationNode(SyntaxNode node) =>
        node is ParameterSyntax
            or VariableDeclaratorSyntax
            or SingleVariableDesignationSyntax
            or ForEachStatementSyntax
            or LocalFunctionStatementSyntax
            or LabeledStatementSyntax
            or CatchDeclarationSyntax;

    private static TNode ReplaceRegionOnNode<TNode>(TNode node, int region) where TNode : SyntaxNode
    {
        foreach (var ann in node.GetAnnotations(OriginalSpanAnnotationKind))
        {
            if (TryParseOriginalSpanData(ann.Data, out var start, out var length, out var treeOrdinal, out _))
            {
                var updated = new SyntaxAnnotation(
                    OriginalSpanAnnotationKind,
                    FormatOriginalSpanData(start, length, treeOrdinal, region));
                return node
                    .WithoutAnnotations(OriginalSpanAnnotationKind)
                    .WithAdditionalAnnotations(updated);
            }
        }

        return node;
    }

    private sealed class RegionStamper(
        int region,
        SemanticModel? model,
        HashSet<ISymbol>? declaredSymbols) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null)
            {
                return null;
            }

            var visited = base.Visit(node);
            if (visited == null || !visited.HasAnnotations(OriginalSpanAnnotationKind))
            {
                return visited;
            }

            if (IsDeclarationNode(visited))
            {
                return ReplaceRegionOnNode(visited, region);
            }

            if (visited is SimpleNameSyntax simpleName)
            {
                if (model == null)
                {
                    return ReplaceRegionOnNode(visited, region);
                }

                var symbol = (model.GetSymbol(simpleName) ?? model.GetDeclaredSymbol<ISymbol>(simpleName))?.OriginalDefinition;
                if (symbol != null && declaredSymbols != null && declaredSymbols.Contains(symbol))
                {
                    return ReplaceRegionOnNode(visited, region);
                }
            }

            return visited;
        }
    }

    /// <summary>
    /// Attaches <see cref="OriginalSpanAnnotationKind"/> to every descendant node of <paramref name="node"/> that does
    /// not already carry one, using each node's current span and, when <paramref name="compilation"/> contains the
    /// node's <see cref="SyntaxTree"/>, the 0-based syntax-tree ordinal.
    /// </summary>
    public static TNode AnnotateOriginalSpans<TNode>(TNode node, Compilation? compilation = null) where TNode : SyntaxNode
    {
        var treeOrdinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(compilation, node.SyntaxTree);
        var annotator = treeOrdinal >= 0 ? new SpanAnnotator(treeOrdinal) : s_spanAnnotator;
        return annotator.Visit(node) as TNode ?? node;
    }

    private static readonly SpanAnnotator s_spanAnnotator = new(-1);

    private sealed class SpanAnnotator(int treeOrdinal) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null)
            {
                return null;
            }

            var visited = base.Visit(node);
            return node.HasAnnotations(OriginalSpanAnnotationKind)
                ? visited
                : visited.WithAdditionalAnnotations(
                    new SyntaxAnnotation(OriginalSpanAnnotationKind, FormatOriginalSpanData(node, treeOrdinal)));
        }
    }
}
