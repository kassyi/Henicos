namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Where a jump statement goes, read from the syntax tree.</summary>
internal static class JumpTargets
{
    /// <summary>
    /// The target of <paramref name="jump"/>: the function of a <c>return</c> (the root of a lowered body that is not
    /// in a function), the loop or switch of a <c>break</c>, the
    /// loop of a <c>continue</c>, the label of a <c>goto</c>, the switch of a <c>goto case</c> or <c>goto default</c>.
    /// False for any other statement, and for a jump whose target is not in the tree (a label that the transpiler
    /// writes itself).
    /// </summary>
    public static bool TryGetTarget(StatementSyntax jump, out UsingExitKind kind,
        [NotNullWhen(true)] out SyntaxNode? target)
    {
        (kind, target) = jump switch
        {
            ReturnStatementSyntax => (UsingExitKind.Return, FunctionOf(jump) ?? jump.SyntaxTree.GetRoot()),
            BreakStatementSyntax => (UsingExitKind.Break, EnclosingTarget(jump, includesSwitch: true)),
            ContinueStatementSyntax => (UsingExitKind.Continue, EnclosingTarget(jump, includesSwitch: false)),
            GotoStatementSyntax { RawKind: (int)SyntaxKind.GotoStatement } label =>
                (UsingExitKind.Goto, FindLabel(label)),
            GotoStatementSyntax => (UsingExitKind.GotoCase, EnclosingSwitch(jump)),
            _ => (UsingExitKind.Return, null)
        };
        return target != null;
    }

    /// <summary>The function, local function, lambda or accessor that holds <paramref name="node"/>.</summary>
    public static SyntaxNode? FunctionOf(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (IsFunction(current))
            {
                return current;
            }
        }

        return null;
    }

    private static bool IsFunction(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax
            or AccessorDeclarationSyntax;

    // The innermost loop (or switch, for a break) around the jump in its function
    private static SyntaxNode? EnclosingTarget(SyntaxNode jump, bool includesSwitch)
    {
        for (var current = jump.Parent; current != null && !IsFunction(current); current = current.Parent)
        {
            if (current is ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax
                    or DoStatementSyntax ||
                (includesSwitch && current is SwitchStatementSyntax))
            {
                return current;
            }
        }

        return null;
    }

    private static SyntaxNode? EnclosingSwitch(SyntaxNode jump)
    {
        for (var current = jump.Parent; current != null && !IsFunction(current); current = current.Parent)
        {
            if (current is SwitchStatementSyntax)
            {
                return current;
            }
        }

        return null;
    }

    // The label of the function that the goto names; a copy that lowering stamped with a region matches the goto of
    // the same region
    private static SyntaxNode? FindLabel(GotoStatementSyntax jump)
    {
        if (jump.Expression is not IdentifierNameSyntax name)
        {
            return null;
        }

        var function = FunctionOf(jump);
        var root = function ?? jump.SyntaxTree.GetRoot();
        var region = LoweringIdentity.GetRegion(jump);
        LabeledStatementSyntax? found = null;
        foreach (var node in root.DescendantNodes())
        {
            if (node is not LabeledStatementSyntax label ||
                label.Identifier.ValueText != name.Identifier.ValueText ||
                FunctionOf(label) != function)
            {
                continue;
            }

            if (LoweringIdentity.GetRegion(label) == region)
            {
                return label;
            }

            found ??= label;
        }

        return found;
    }
}
