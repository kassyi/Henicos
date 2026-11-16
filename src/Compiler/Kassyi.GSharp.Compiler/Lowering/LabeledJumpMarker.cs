namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Marks each loop or <c>switch</c> statement that a labeled <c>break</c> or <c>continue</c> names and that holds a split
/// point, and each of those jumps, with the same number (<see cref="LoweringAnnotations.LabeledJumpTargetAnnotationKind"/>).
/// </summary>
/// <remarks>
/// [WHY] <see cref="LabeledStatementSplitter"/> detaches the label of a statement that holds a split point
/// (<c>L: s</c> becomes <c>L: ; s</c>), after which the name of a jump no longer finds its statement in the tree. The
/// number does: it is on the statement and on the jumps, and it stays valid when a pass moves the statements around.
/// The tree is not changed otherwise. A target without a split point is not marked: no pass flattens it, so it keeps its
/// label, and the jumps to it reach the emitter by name (#1990). <see cref="LabeledJumpRewriter"/> reads the marks.
/// </remarks>
internal static class LabeledJumpMarker
{
    /// <summary>Marks the targets of <paramref name="body"/> that hold a split point, and the jumps to them.</summary>
    public static BlockSyntax Mark(BlockSyntax body)
    {
        var numbers = new Dictionary<SyntaxNode, int>();
        var jumps = new Dictionary<SyntaxNode, int>();
        foreach (var node in body.DescendantNodes())
        {
            if (node is not (BreakStatementSyntax or ContinueStatementSyntax) ||
                LabeledJumpSyntax.FindTarget((StatementSyntax)node) is not { } target ||
                !BarrierHelper.ContainsPhaseBarrier(target))
            {
                continue;
            }

            if (!numbers.TryGetValue(target, out var number))
            {
                number = numbers.Count;
                numbers.Add(target, number);
            }

            jumps.Add(node, number);
        }

        if (numbers.Count == 0)
        {
            return body;
        }

        var marked = ImmutableArray.CreateBuilder<SyntaxNode>(numbers.Count + jumps.Count);
        marked.AddRange(numbers.Keys);
        marked.AddRange(jumps.Keys);
        return body.ReplaceNodes(
            marked.ToImmutable(),
            (original, rewritten) => rewritten.WithAdditionalAnnotations(
                new SyntaxAnnotation(
                    LoweringAnnotations.LabeledJumpTargetAnnotationKind,
                    (numbers.TryGetValue(original, out var targetNumber) ? targetNumber : jumps[original]).ToString(
                        CultureInfo.InvariantCulture
                    )
                )
            )
        );
    }

    /// <summary>The number that <see cref="Mark"/> put on <paramref name="node"/>, or -1 when it has none.</summary>
    public static int GetNumber(SyntaxNode node)
    {
        foreach (var annotation in node.GetAnnotations(LoweringAnnotations.LabeledJumpTargetAnnotationKind))
        {
            if (int.TryParse(annotation.Data, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }
        }

        return -1;
    }

    /// <summary>
    /// The loop or <c>switch</c> statement that the labeled <paramref name="jump"/> names: the enclosing statement that
    /// carries its number when <see cref="Mark"/> marked it, otherwise the one that
    /// <see cref="LabeledJumpSyntax.FindTarget"/> finds by the name. <see langword="null"/> when there is none.
    /// </summary>
    public static SyntaxNode? FindTarget(StatementSyntax jump)
    {
        var number = GetNumber(jump);
        if (number < 0)
        {
            return LabeledJumpSyntax.FindTarget(jump);
        }

        foreach (var ancestor in jump.Ancestors())
        {
            if (GetNumber(ancestor) == number)
            {
                return ancestor;
            }
        }

        return null;
    }
}
