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

    // var <name> = <value>; with the spilled type span of spilledSource on the declarator, as the passes that hoist
    // split points out of expressions declare their temporaries; NormalizeWhitespace gives it the usual spacing
    public static LocalDeclarationStatementSyntax DeclareTemporary(
        string name,
        ExpressionSyntax value,
        SyntaxNode? spilledSource = null,
        Compilation? compilation = null,
        params SyntaxAnnotation[] declaratorAnnotations)
    {
        var declarator = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
            .WithInitializer(SyntaxFactory.EqualsValueClause(value))
            .WithAdditionalAnnotations(declaratorAnnotations);
        if (spilledSource != null)
        {
            declarator = declarator.WithSpilledTypeSpanFrom(spilledSource, compilation);
        }

        return DeclareLocal(declarator).NormalizeWhitespace();
    }

    // <type> x; for the argument 'out <type> x' of a call that splits, and the reference x that replaces the argument
    // expression; both keep the original span of the designation, which names the local
    public static (LocalDeclarationStatementSyntax Declaration, IdentifierNameSyntax Reference) DeclareOutVariable(
        DeclarationExpressionSyntax declaration, SingleVariableDesignationSyntax designation)
    {
        var annotations = ImmutableArray.CreateRange(
            designation.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)
        );
        var identifier = designation.Identifier.WithoutTrivia();
        var declarator = SyntaxFactory.VariableDeclarator(identifier).WithAdditionalAnnotations(annotations);
        var statement = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                declaration.Type.WithoutTrivia(),
                SyntaxFactory.SingletonSeparatedList(declarator)
            )
        ).NormalizeWhitespace();
        var reference = SyntaxFactory.IdentifierName(identifier)
            .WithAdditionalAnnotations(annotations)
            .WithTriviaFrom(declaration);
        return (statement, reference);
    }
}
