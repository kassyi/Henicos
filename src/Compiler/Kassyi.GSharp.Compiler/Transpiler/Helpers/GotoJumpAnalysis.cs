namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>Finds the local declarations that a forward <c>goto</c> can jump past.</summary>
/// <remarks>
/// [WHY] C# lets a <c>goto</c> jump forward past a declaration with an initializer, because definite assignment makes
/// sure that the variable is assigned again before it is read after the label. In C++ that jump is ill-formed
/// ([stmt.dcl]): g++ and clang reject it, nvcc warns (#546-D). Such a declaration is emitted as a declaration and an
/// assignment (<c>int x; x = 1;</c>), or a stack array as a declaration and a zero fill
/// (<c>int a[4]; memset(a, 0, sizeof(a))</c>), which keeps the C# meaning. A ref local, a <c>using</c> declaration, a ref
/// struct local that is not a <c>stackalloc</c> (a <c>Span&lt;T&gt;</c> from an array), a <c>ReadOnlySpan&lt;T&gt;</c>
/// <c>stackalloc</c> (a const array) and an array with an initializer list are not split, so a goto past them stays
/// ill-formed C++.
/// </remarks>
internal static class GotoJumpAnalysis
{
    /// <summary>
    /// Whether a <c>goto</c> before <paramref name="declaration"/> in its statement list (nested statements included)
    /// targets a label after it in the same list. C# labels cannot shadow each other (CS0158), so the name decides.
    /// </summary>
    public static bool MayBeJumpedOver(LocalDeclarationStatementSyntax declaration)
    {
        var statements = declaration.Parent switch
        {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            _ => default
        };

        var index = statements.IndexOf(declaration);
        if (index < 0)
        {
            return false;
        }

        for (var after = index + 1; after < statements.Count; after++)
        {
            var current = statements[after];
            while (current is LabeledStatementSyntax labeled)
            {
                if (HasGotoBefore(statements, index, labeled.Identifier.ValueText))
                {
                    return true;
                }

                current = labeled.Statement;
            }
        }

        return false;
    }

    private static bool HasGotoBefore(SyntaxList<StatementSyntax> statements, int end, string label)
    {
        for (var i = 0; i < end; i++)
        {
            // A goto in a lambda or a local function cannot leave it
            foreach (var node in statements[i].DescendantNodesAndSelf(
                         static n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
            {
                if (node is GotoStatementSyntax { RawKind: (int)SyntaxKind.GotoStatement, Expression: IdentifierNameSyntax target } &&
                    target.Identifier.ValueText == label)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
