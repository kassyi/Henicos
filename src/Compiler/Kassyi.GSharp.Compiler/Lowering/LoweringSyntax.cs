namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The statements that the lowering passes build in many places: jumps, labels and <c>var</c> locals.
/// </summary>
internal static class LoweringSyntax
{
    // goto <label>;
    public static GotoStatementSyntax Goto(string label) =>
        SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(label));

    // <label>: ;
    public static LabeledStatementSyntax EmptyLabel(string label) =>
        SyntaxFactory.LabeledStatement(label, SyntaxFactory.EmptyStatement());

    // var <name> = <value>;
    public static LocalDeclarationStatementSyntax DeclareLocal(string name, ExpressionSyntax value) =>
        DeclareLocal(
            SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
                .WithInitializer(SyntaxFactory.EqualsValueClause(value))
        );

    // var <declarator>;
    public static LocalDeclarationStatementSyntax DeclareLocal(VariableDeclaratorSyntax declarator) =>
        SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(declarator)
            )
        );
}
