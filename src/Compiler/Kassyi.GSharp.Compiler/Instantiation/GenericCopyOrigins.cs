namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Where the nodes of the closed copies are in the user's source: a copy lives in a tree of the derived compilation
/// (<see cref="GenericCopies.Create(Compilation, ImmutableArray{GenericCopyPlan})"/>), and a diagnostic about it is
/// reported at the node of the generic declaration it came from, in the tree of the user's compilation.
/// </summary>
/// <remarks>
/// [WHY] Keyed by the derived tree, which belongs to one derived compilation only; the table drops it together with that
/// tree. <see cref="SemanticModelExtensions.GetReportLocation"/> asks here, so that every reader of a report location
/// gets the user's source without knowing about copies.
/// </remarks>
internal static class GenericCopyOrigins
{
    private static readonly ConditionalWeakTable<SyntaxTree, SyntaxTree> s_originals = new();

    /// <summary>Records that <paramref name="derived"/> is <paramref name="original"/> with copies added.</summary>
    public static void Register(SyntaxTree derived, SyntaxTree original) =>
        s_originals.GetValue(derived, _ => original);

    /// <summary>
    /// The location in the user's tree of the declaration node that <paramref name="node"/>, or its nearest ancestor
    /// with a <see cref="GenericCopyEmitter.CopyOriginAnnotationKind"/> annotation, came from; false when
    /// <paramref name="node"/> is not a node of a copy.
    /// </summary>
    public static bool TryGetLocation(SyntaxNode node, [NotNullWhen(true)] out Location? location)
    {
        location = null;
        if (!s_originals.TryGetValue(node.SyntaxTree, out var original))
        {
            return false;
        }

        for (var current = node; current != null; current = current.Parent)
        {
            foreach (var annotation in current.GetAnnotations(GenericCopyEmitter.CopyOriginAnnotationKind))
            {
                if (LoweringAnnotations.TryParseOriginalSpanData(annotation.Data, out var start, out var length, out _,
                        out _))
                {
                    location = Location.Create(original, new TextSpan(start, length));
                    return true;
                }
            }
        }

        return false;
    }
}
