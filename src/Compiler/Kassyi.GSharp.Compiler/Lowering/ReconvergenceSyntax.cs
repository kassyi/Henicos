namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Syntax lookups of <see cref="ReconvergenceInserter"/> that read only the tree: the label a goto names, whether a
/// node lies inside another, and the statement a break or continue jumps out of.
/// </summary>
internal static class ReconvergenceSyntax
{
    internal static LabeledStatementSyntax? FindLabel(SyntaxNode from, string name)
    {
        foreach (var ancestor in from.Ancestors())
        {
            var statements = ancestor switch
            {
                BlockSyntax block => block.Statements,
                SwitchSectionSyntax section => section.Statements,
                _ => default
            };
            foreach (var statement in statements)
            {
                if (FindLabelInChain(statement, name) is { } labeled)
                {
                    return labeled;
                }
            }

            if (ancestor is MemberDeclarationSyntax or AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax)
            {
                break;
            }
        }

        return null;
    }

    internal static LabeledStatementSyntax? FindLabelInChain(StatementSyntax statement, string name)
    {
        for (var current = statement; current is LabeledStatementSyntax labeled; current = labeled.Statement)
        {
            if (labeled.Identifier.ValueText == name)
            {
                return labeled;
            }
        }

        return null;
    }

    internal static bool IsInside(SyntaxNode node, SyntaxNode extent)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            if (current == extent)
            {
                return true;
            }
        }

        return false;
    }

    // The loop (or the switch, for a break) that a break or continue jumps out of
    internal static SyntaxNode? FindJumpTarget(SyntaxNode jump, bool loopOnly)
    {
        foreach (var ancestor in jump.Ancestors())
        {
            switch (ancestor)
            {
                case WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax:
                    return ancestor;
                case SwitchStatementSyntax when !loopOnly:
                    return ancestor;
                case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax:
                    return null;
            }
        }

        return null;
    }
}
