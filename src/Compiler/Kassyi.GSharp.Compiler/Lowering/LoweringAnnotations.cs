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
    /// Annotation kind that marks a lowered <c>foreach</c> loop index declarator
    /// (<c>__gsharp_foreach_i_&lt;id&gt;</c>) as <see cref="int"/> when hoisted into a context struct.
    /// </summary>
    public const string ForEachIndexAnnotationKind = "GSharpForEachIndex";

    internal static string FormatOriginalSpanData(SyntaxNode node, int treeOrdinal = -1) =>
        treeOrdinal >= 0
            ? $"{node.SpanStart},{node.Span.Length},{treeOrdinal}"
            : $"{node.SpanStart},{node.Span.Length}";

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
