namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;

/// <summary>Finds the local declarations that a <c>goto</c> can jump past into their C++ scope.</summary>
/// <remarks>
/// [WHY] C# lets a <c>goto</c> jump past a declaration with an initializer, because definite assignment makes sure that
/// the variable is assigned again before it is read after the label. In C++ a jump into the scope of a declaration with
/// an initializer is ill-formed ([stmt.dcl]): g++ and clang reject it, nvcc warns (#546-D). Such a declaration is
/// emitted as a declaration and an assignment (<c>int x; x = 1;</c>), or a stack array as a declaration and a zero fill
/// (<c>int a[4]; memset(a, 0, sizeof(a))</c>), which keeps the C# meaning. The scope is the C++ one
/// (<see cref="CppBlockScopes"/>): a nested block written without braces puts its declarations in the scope of the list
/// around it, and a goto from another switch section enters the braces of the section from outside. A ref local, a
/// <c>using</c> declaration, a ref struct local that is not a <c>stackalloc</c> (a <c>Span&lt;T&gt;</c> from an array),
/// a <c>ReadOnlySpan&lt;T&gt;</c> <c>stackalloc</c> (a const array) and an array with an initializer list are not
/// split, so a goto past them stays ill-formed C++.
/// </remarks>
internal static class GotoJumpAnalysis
{
    /// <summary>
    /// Whether a <c>goto</c> targets a label after <paramref name="declaration"/> in its C++ scope while the goto
    /// itself is before the declaration or outside that scope. C# labels cannot shadow each other (CS0158), so the
    /// name decides.
    /// </summary>
    public static bool MayBeJumpedOver(LocalDeclarationStatementSyntax declaration) =>
        FindJump(declaration) != Jump.None;

    /// <summary>
    /// Whether a <c>goto</c> in another section of the switch jumps past <paramref name="declaration"/>, into the
    /// braces of its section: a declaration at the start of the section would be jumped past too.
    /// </summary>
    public static bool IsEnteredFromOtherSection(LocalDeclarationStatementSyntax declaration) =>
        FindJump(declaration) == Jump.FromOutsideScope;

    private enum Jump
    {
        None,
        WithinScope,
        FromOutsideScope
    }

    private static Jump FindJump(LocalDeclarationStatementSyntax declaration)
    {
        if (CppBlockScopes.ScopeOf(declaration) is not { } scope)
        {
            return Jump.None;
        }

        var jump = Jump.None;
        foreach (var node in scope.DescendantNodes(IsOutsideNestedFunction))
        {
            // A label in front of the declaration (or of a statement before it) is not in its scope
            if (node is not LabeledStatementSyntax label || label.SpanStart < declaration.Span.End ||
                LabelContainer(label) is not { } container)
            {
                continue;
            }

            foreach (var candidate in container.DescendantNodes(IsOutsideNestedFunction))
            {
                if (candidate is not GotoStatementSyntax
                    {
                        RawKind: (int)SyntaxKind.GotoStatement,
                        Expression: IdentifierNameSyntax target
                    } jumpStatement ||
                    target.Identifier.ValueText != label.Identifier.ValueText)
                {
                    continue;
                }

                if (!scope.Span.Contains(jumpStatement.Span))
                {
                    return Jump.FromOutsideScope;
                }

                if (jumpStatement.SpanStart < declaration.SpanStart)
                {
                    jump = Jump.WithinScope;
                }
            }
        }

        return jump;
    }

    // A goto in a lambda or a local function cannot leave it, and a label there cannot be reached from outside
    private static bool IsOutsideNestedFunction(SyntaxNode node) =>
        node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);

    // Where a goto to the label may be: its block, or the whole switch for a label of a switch section
    private static SyntaxNode? LabelContainer(LabeledStatementSyntax label)
    {
        SyntaxNode node = label;
        while (node.Parent is LabeledStatementSyntax outer)
        {
            node = outer;
        }

        return node.Parent switch
        {
            BlockSyntax block => block,
            SwitchSectionSyntax { Parent: SwitchStatementSyntax switchStatement } => switchStatement,
            _ => null
        };
    }
}
