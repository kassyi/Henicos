using Kassyi.GSharp.Compiler.Ir;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The syntax of one statement of the prepared body by the span of the source it stands for, so that a node of the IR
/// finds the syntax to copy (<see cref="IrLoweredSyntaxPrinter"/>).
/// </summary>
/// <remarks>
/// The key is the original span of a node (its <see cref="LoweringAnnotations.OriginalSpanAnnotationKind"/>
/// annotation), in the tree of the method and outside any inlined copy. Of several nodes with one span (an argument and
/// its expression), the innermost is kept, as <see cref="SyntaxNode.FindNode"/> picks it: the expression an operation
/// stands for. An implicit conversion has the span of its operand and so finds the operand, which is right: the emitter
/// writes the conversion from the context.
/// </remarks>
internal sealed class IrSyntaxIndex
{
    private readonly Dictionary<TextSpan, SyntaxNode> _nodes;
    private readonly int _treeOrdinal;

    private IrSyntaxIndex(Dictionary<TextSpan, SyntaxNode> nodes, int treeOrdinal)
    {
        _nodes = nodes;
        _treeOrdinal = treeOrdinal;
    }

    /// <summary>Indexes the nodes of <paramref name="statement"/> once.</summary>
    /// <param name="statement">A statement of the prepared body.</param>
    /// <param name="treeOrdinal">The ordinal of the tree of the method in the compilation.</param>
    public static IrSyntaxIndex Build(StatementSyntax statement, int treeOrdinal)
    {
        var nodes = new Dictionary<TextSpan, SyntaxNode>();
        // Outer nodes come first, so that an inner node of the same span replaces them
        foreach (var node in statement.DescendantNodesAndSelf())
        {
            if (OriginalSpanOf(node, treeOrdinal) is { } span)
            {
                nodes[span] = node;
            }
        }

        return new IrSyntaxIndex(nodes, treeOrdinal);
    }

    /// <summary>The syntax of <paramref name="source"/>, when the statement has one.</summary>
    public bool TryGet(IrSource source, [NotNullWhen(true)] out SyntaxNode? node)
    {
        node = null;
        return source.Region == 0 && source.TreeOrdinal == _treeOrdinal && _nodes.TryGetValue(source.Span, out node);
    }

    /// <summary>
    /// The original span of <paramref name="node"/> in the tree <paramref name="treeOrdinal"/>, outside any inlined
    /// copy; <see langword="null"/> when it has none.
    /// </summary>
    public static TextSpan? OriginalSpanOf(SyntaxNode node, int treeOrdinal)
    {
        foreach (var annotation in node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
        {
            if (LoweringAnnotations.TryParseOriginalSpanData(annotation.Data, out var start, out var length,
                    out var tree, out var region) &&
                region == 0 && (tree == treeOrdinal || tree < 0))
            {
                return new TextSpan(start, length);
            }
        }

        return null;
    }
}
