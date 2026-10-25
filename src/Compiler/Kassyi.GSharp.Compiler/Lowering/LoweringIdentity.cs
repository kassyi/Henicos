namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Reads the copy (region) ordinal stamped onto lowered syntax nodes by pre-lowering and inline expansions.
/// </summary>
public static class LoweringIdentity
{
    /// <summary>
    /// Returns the 1-based region number recorded in the <see cref="LoweringAnnotations.OriginalSpanAnnotationKind"/>
    /// annotation of <paramref name="node"/>, or <c>0</c> when the node has no region.
    /// </summary>
    public static int GetRegion(SyntaxNode node)
    {
        if (!node.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
        {
            return 0;
        }

        foreach (var ann in node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
        {
            if (LoweringAnnotations.TryParseOriginalSpanData(ann.Data, out _, out _, out _, out var region))
            {
                return region > 0 ? region : 0;
            }
        }

        return 0;
    }
}
