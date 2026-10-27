namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Removes from the managed body of a device function's state machine the statements that follow, in the same list, a
/// statement whose end no path reaches: a jump (<c>goto</c>, <c>return</c>, <c>break</c>, <c>continue</c>), or a
/// block that ends with one.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] The lowering writes the jump to the next state after a loop body that already jumps back, and a second block
/// of a state after the block that returns. C# reports each as CS0162, which an exporting library (#538) would show for
/// code it did not write. The rest of a state after the <c>throw</c> of an unsupported intrinsic (<c>Gpu.InlinePtx</c>)
/// stays: removing it leaves its locals unused (CS0219); the intrinsic itself is #562.
/// </para>
/// <para>
/// A labeled statement may be the target of a jump, so the removal stops at it; a local declaration may be used after
/// such a label, so it stops there too. Only what C# itself calls unreachable is removed, so the behavior is the same.
/// </para>
/// </remarks>
internal sealed class UnreachableStatementRemover : CSharpSyntaxRewriter
{
    public static readonly UnreachableStatementRemover Instance = new();

    private UnreachableStatementRemover()
    {
    }

    public override SyntaxNode? VisitBlock(BlockSyntax node)
    {
        var visited = (BlockSyntax)(base.VisitBlock(node) ?? node);
        return visited.WithStatements(RemoveUnreachable(visited.Statements));
    }

    public override SyntaxNode? VisitSwitchSection(SwitchSectionSyntax node)
    {
        var visited = (SwitchSectionSyntax)(base.VisitSwitchSection(node) ?? node);
        return visited.WithStatements(RemoveUnreachable(visited.Statements));
    }

    private static SyntaxList<StatementSyntax> RemoveUnreachable(SyntaxList<StatementSyntax> statements)
    {
        for (var i = 0; i < statements.Count - 1; i++)
        {
            if (!EndsUnconditionally(statements[i]))
            {
                continue;
            }

            var end = i + 1;
            while (end < statements.Count && statements[end] is not (LabeledStatementSyntax or LocalDeclarationStatementSyntax))
            {
                end++;
            }

            if (end == i + 1)
            {
                continue;
            }

            var kept = new SyntaxList<StatementSyntax>();
            for (var j = 0; j < statements.Count; j++)
            {
                if (j <= i || j >= end)
                {
                    kept = kept.Add(statements[j]);
                }
            }

            return RemoveUnreachable(kept);
        }

        return statements;
    }

    // A jump, or a non-empty block whose last statement ends unconditionally
    private static bool EndsUnconditionally(StatementSyntax statement) => statement switch
    {
        GotoStatementSyntax or ReturnStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax => true,
        LabeledStatementSyntax labeled => EndsUnconditionally(labeled.Statement),
        BlockSyntax { Statements.Count: > 0 } block => EndsUnconditionally(block.Statements[block.Statements.Count - 1]),
        _ => false
    };
}
