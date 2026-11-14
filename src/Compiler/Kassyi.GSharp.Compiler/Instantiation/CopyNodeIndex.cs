namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Numbers the nodes of a generic declaration so that each node of its closed copy names the node it came from: the
/// copy is outside the compilation of the declaration, and only the declaration can be asked what a node binds to.
/// </summary>
/// <remarks>
/// The number is the position in a pre-order walk (<see cref="List"/>), which <see cref="Annotate"/> follows as well;
/// a node that the copy writes anew (<see cref="GenericCopyEmitter"/>) carries the number of the node it replaces at
/// its root only.
/// </remarks>
internal static class CopyNodeIndex
{
    /// <summary>The annotation kind; its data is the node number.</summary>
    public const string AnnotationKind = "GSharpGenericCopyNode";

    /// <summary>The nodes of <paramref name="declaration"/> in pre-order, itself first.</summary>
    public static SyntaxNode[] List(SyntaxNode declaration) =>
        declaration.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .ToArray();

    /// <summary>
    /// <paramref name="declaration"/> with every node annotated with its position in <see cref="List"/>.
    /// </summary>
    public static TNode Annotate<TNode>(TNode declaration) where TNode : SyntaxNode =>
        new Annotator().Visit(declaration) as TNode ??
        throw new InvalidOperationException("Annotating the nodes of a declaration changed its kind.");

    /// <summary>The annotation that names node <paramref name="index"/>.</summary>
    public static SyntaxAnnotation Create(int index) =>
        new(AnnotationKind, index.ToString(CultureInfo.InvariantCulture));

    /// <summary>The number of the node <paramref name="node"/> came from, if it carries one.</summary>
    public static bool TryGet(SyntaxNode node, out int index)
    {
        foreach (var annotation in node.GetAnnotations(AnnotationKind))
        {
            return int.TryParse(annotation.Data, NumberStyles.None, CultureInfo.InvariantCulture, out index);
        }

        index = -1;
        return false;
    }

    // Numbers a node before its children, as DescendantNodesAndSelf lists them
    private sealed class Annotator : CSharpSyntaxRewriter
    {
        private int _next;

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null)
            {
                return null;
            }

            var index = _next++;
            return base.Visit(node)?.WithAdditionalAnnotations(Create(index));
        }
    }
}
