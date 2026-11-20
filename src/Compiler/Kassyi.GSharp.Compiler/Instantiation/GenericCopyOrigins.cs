namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Where the nodes of the closed copies are in the user's source: a copy lives in a tree of the derived compilation
/// (<see cref="GenericCopies.Create(Compilation, ImmutableArray{GenericCopyPlan})"/>), and a diagnostic about it is
/// reported at the node of the generic declaration it came from, in the tree of the user's compilation.
/// </summary>
/// <remarks>
/// [WHY] Keyed by the derived tree, which belongs to one derived compilation only; the table drops it together with
/// that tree. <see cref="SemanticModelExtensions.GetReportLocation"/> asks here, so that every report of a node of a
/// copy is in the user's source; a semantic lookup takes the node of the copy instead
/// (<see cref="SemanticModelExtensions.GetCompilationNode"/>).
/// </remarks>
internal static class GenericCopyOrigins
{
    private static readonly ConditionalWeakTable<SyntaxTree, SyntaxTree> s_originals = new();

    /// <summary>Records that <paramref name="derived"/> is <paramref name="original"/> with copies added.</summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="original"/> is itself a derived tree: its spans are not the user's, so the copies of a derived
    /// compilation could not report in the user's source. One derived compilation holds every copy of a run.
    /// </exception>
    public static void Register(SyntaxTree derived, SyntaxTree original)
    {
        if (s_originals.TryGetValue(original, out _))
        {
            throw new InvalidOperationException(
                $"The closed copies are added to '{original.FilePath}' a second time; " +
                "make them in one derived compilation.");
        }

        s_originals.GetValue(derived, _ => original);
    }

    /// <summary>
    /// The location in the user's tree of the declaration node that <paramref name="node"/>, a node of a copy, came
    /// from; false when <paramref name="node"/> is not a node of a copy.
    /// </summary>
    /// <remarks>
    /// Every node of a copy carries the annotation (<see cref="GenericCopyEmitter"/> stamps each node it writes), so
    /// the node's own annotation answers; a node of a derived tree outside the copies has none and keeps its own place.
    /// </remarks>
    public static bool TryGetLocation(SyntaxNode node, [NotNullWhen(true)] out Location? location)
    {
        location = null;
        if (!s_originals.TryGetValue(node.SyntaxTree, out var original))
        {
            return false;
        }

        foreach (var annotation in node.GetAnnotations(GenericCopyEmitter.CopyOriginAnnotationKind))
        {
            if (LoweringAnnotations.TryParseOriginalSpanData(annotation.Data, out var start, out var length, out _,
                    out _))
            {
                location = Location.Create(original, new TextSpan(start, length));
                return true;
            }
        }

        return false;
    }
}
