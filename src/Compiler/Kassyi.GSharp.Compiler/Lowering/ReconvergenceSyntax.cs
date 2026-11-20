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
            if (ancestor is SwitchSectionSyntax section)
            {
                if (section.Parent is SwitchStatementSyntax switchStmt)
                {
                    foreach (var sec in switchStmt.Sections)
                    {
                        foreach (var statement in sec.Statements)
                        {
                            if (FindLabelInChain(statement, name) is { } labeled)
                            {
                                return labeled;
                            }
                        }
                    }
                }

                continue;
            }

            var statements = ancestor switch
            {
                BlockSyntax block => block.Statements,
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

    // Whether the break or continue leaves the loop before its body ends: a break of the loop itself, or a labeled jump to
    // a statement around it. A jump to a statement inside the loop stays in it.
    internal static bool LeavesLoop(SyntaxNode jump, SyntaxNode loop) =>
        jump switch
        {
            BreakStatementSyntax => FindJumpTarget(jump, loopOnly: false) is { } target &&
                                    (target == loop || IsInside(loop, target)),
            ContinueStatementSyntax => LabeledJumpSyntax.GetLabelName((StatementSyntax)jump) != null &&
                                       FindJumpTarget(jump, loopOnly: true) is { } target &&
                                       target != loop &&
                                       IsInside(loop, target),
            _ => false
        };

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

    // The loop (or the switch, for a break) that a break or continue jumps out of: the one it names when it has a label
    // (LabeledJumpMarker.FindTarget), otherwise the innermost
    internal static SyntaxNode? FindJumpTarget(SyntaxNode jump, bool loopOnly)
    {
        if (jump is StatementSyntax statement && LabeledJumpSyntax.GetLabelName(statement) != null)
        {
            return LabeledJumpMarker.FindTarget(statement);
        }

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

    /// <summary>
    /// Determines whether any section of <paramref name="switchStatement"/> contains a jump out of the switch
    /// (<c>break</c> targeting this switch, or <c>goto case</c> / <c>goto default</c> targeting this switch)
    /// that is nested inside a non-transparent statement (such as <c>if</c>, a loop, or an inner switch).
    /// </summary>
    /// <remarks>
    /// Direct exits under a section (<c>break;</c> or <c>goto case</c> directly in a section or inside
    /// transparent blocks/labels) are executed by all lanes reaching that section and do not cause intra-section
    /// divergence. Jumps inside nested lambdas or local functions are ignored.
    /// </remarks>
    internal static bool HasNestedExit(SwitchStatementSyntax switchStatement) =>
        switchStatement.Sections
            .AsStackEnumerable()
            .Any(switchStatement, static (section, @switch) =>
                section.DescendantNodes(static n => n is not
                        (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                    .AsStackEnumerable()
                    .Any((@switch, section), static (node, state) =>
                        IsSwitchExit(node, state.@switch) && HasInterveningStatement(node, state.section)));

    private static bool IsSwitchExit(SyntaxNode node, SwitchStatementSyntax switchStatement) =>
        node switch
        {
            BreakStatementSyntax => FindJumpTarget(node, loopOnly: false) == switchStatement,
            GotoStatementSyntax gotoStatement when gotoStatement.IsKind(SyntaxKind.GotoCaseStatement) ||
                                                   gotoStatement.IsKind(SyntaxKind.GotoDefaultStatement) =>
                EnclosingSwitchIs(gotoStatement, switchStatement),
            GotoStatementSyntax gotoStatement when gotoStatement.IsKind(SyntaxKind.GotoStatement) =>
                IsCrossSectionGoto(gotoStatement, switchStatement),
            _ => false
        };

    private static bool IsCrossSectionGoto(GotoStatementSyntax gotoStatement, SwitchStatementSyntax switchStatement)
    {
        if (gotoStatement.Expression is IdentifierNameSyntax { Identifier.ValueText: var name } &&
            FindLabel(gotoStatement, name) is { } label)
        {
            SwitchSectionSyntax? targetSection = null;
            for (var c = label.Parent; c != null; c = c.Parent)
            {
                if (c is SwitchSectionSyntax sec)
                {
                    targetSection = sec;
                    break;
                }
            }

            SwitchSectionSyntax? sourceSection = null;
            for (var c = gotoStatement.Parent; c != null; c = c.Parent)
            {
                if (c is SwitchSectionSyntax sec)
                {
                    sourceSection = sec;
                    break;
                }
            }

            return targetSection != null && targetSection != sourceSection && targetSection.Parent == switchStatement;
        }

        return false;
    }

    internal static bool EnclosingSwitchIs(SyntaxNode node, SwitchStatementSyntax switchStatement)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is SwitchStatementSyntax s)
            {
                return s == switchStatement;
            }

            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                or MemberDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static bool HasInterveningStatement(SyntaxNode node, SwitchSectionSyntax section)
    {
        for (var current = node.Parent; current != null && current != section; current = current.Parent)
        {
            if (current is StatementSyntax and not (BlockSyntax or LabeledStatementSyntax or
                    CheckedStatementSyntax or UnsafeStatementSyntax))
            {
                return true;
            }
        }

        return false;
    }
}
