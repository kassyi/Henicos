namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;

/// <summary>The C++ scope of a statement, where the transpiler writes nested blocks without braces.</summary>
/// <remarks>
/// [WHY] A nested block in a statement list is written without braces (lowering wraps the statements of one scope in
/// blocks), so its declarations land in the C++ scope of the list around it. A switch section is written in braces of
/// its own, while C# makes the whole switch block one scope. Checks of C++ scope rules (a jump past an initialization,
/// [stmt.dcl]) use these scopes, not the C# ones.
/// </remarks>
internal static class CppBlockScopes
{
    /// <summary>
    /// Whether <paramref name="block"/> is written without braces, in the C++ scope of the statement list around it: a
    /// nested block of a block or a switch section that declares no <c>using</c> local (in a block) and is no
    /// synthesized iterator scope, or the statement of a label.
    /// </summary>
    public static bool IsWrittenWithoutBraces(BlockSyntax block) => block.Parent switch
    {
        LabeledStatementSyntax => true,
        BlockSyntax => !UsingTranspiler.DeclaresUsing(block) &&
                       !block.HasAnnotations(LoweringAnnotations.ScopedBlockAnnotationKind),
        SwitchSectionSyntax => !block.HasAnnotations(LoweringAnnotations.ScopedBlockAnnotationKind),
        _ => false
    };

    /// <summary>
    /// The block or switch section whose braces enclose <paramref name="statement"/> in C++: its statement list, seen
    /// through labels and blocks written without braces. <see langword="null"/> when the statement is in no list.
    /// </summary>
    public static SyntaxNode? ScopeOf(StatementSyntax statement)
    {
        for (SyntaxNode node = statement; ;)
        {
            switch (node.Parent)
            {
                case LabeledStatementSyntax labeled:
                    node = labeled;
                    break;
                case BlockSyntax block when IsWrittenWithoutBraces(block):
                    node = block;
                    break;
                case BlockSyntax or SwitchSectionSyntax:
                    return node.Parent;
                default:
                    return null;
            }
        }
    }
}
