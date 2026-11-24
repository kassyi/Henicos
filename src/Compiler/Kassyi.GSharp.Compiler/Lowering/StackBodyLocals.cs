namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Makes the locals of a body lowered for the CPU stack models (<see cref="AstLoweringPipeline.LowerForStack"/>) valid
/// where the body is written as it is, in C# or C++: every local is declared in a scope that holds all its uses, with
/// its type written out and a value.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] The state machine moves every local into its frame, so the passes it shares with the stacks may leave a local
/// where the source would not: <see cref="ConditionalEvaluationRewriter"/> moves the right operand of <c>&amp;&amp;</c>,
/// <c>||</c> and <c>?:</c> into an <c>if</c> block of its own, with the <c>out</c> variable it declares, while the uses
/// stay after the block (<c>if (a &amp;&amp; F(out uint m)) Use(m);</c>); the declaration that
/// <see cref="LoweringSyntax.DeclareOutVariable"/> splits from <c>out var m</c> keeps <c>var</c> without a value; and a
/// local that C# finds definitely assigned through <c>&amp;&amp;</c> (<c>uint v; if (a &amp;&amp; (v = G()) != 0u)
/// Use(v);</c>) is no longer so once the operands are statements.
/// </para>
/// <para>
/// So a declaration whose uses lie outside the block or switch section it is in moves to the innermost enclosing one
/// that holds them all, in front of the first statement there that holds it or a use; an <c>out</c> variable declared
/// in an argument becomes a declaration there and the argument names it. Every declaration without a value gets
/// <c>default(T)</c>, with <c>var</c> replaced by the type of the local. The program reads the same values: C# assigns
/// a local before any read in the source, and the move stays inside the scope the source gives the local, so no loop
/// starts between the old and the new place.
/// </para>
/// </remarks>
internal static class StackBodyLocals
{
    /// <summary>The body with its locals declared where every use sees them, typed and with a value.</summary>
    /// <param name="body">The body lowered for the stacks.</param>
    /// <param name="bodyModel">The semantic model of the tree of the method.</param>
    /// <exception cref="InvalidOperationException">
    /// A local would move into a scope that declares another local of the same name.
    /// </exception>
    public static BlockSyntax Normalize(BlockSyntax body, SemanticModel bodyModel)
    {
        var plan = PlanMoves(body, bodyModel);
        var moved = plan.ByDeclaration.Count == 0
            ? body
            : new MoveRewriter(plan).Visit(body) as BlockSyntax
              ?? throw new InvalidOperationException("Moving the locals of a stack body removed the body.");
        return new InitializerRewriter(bodyModel).Visit(moved) as BlockSyntax
               ?? throw new InvalidOperationException("Initializing the locals of a stack body removed the body.");
    }

    // One local to move: its declaration (a declarator of a local declaration statement or the designation of an out
    // argument), its declared type, and the statement of the target scope it goes in front of
    private sealed record Move(SyntaxNode Declaration, ILocalSymbol Local, TypeSyntax Type, StatementSyntax Anchor);

    private sealed class MovePlan
    {
        public Dictionary<SyntaxNode, Move> ByDeclaration { get; } = [];

        public Dictionary<StatementSyntax, List<Move>> ByAnchor { get; } = [];
    }

    private static MovePlan PlanMoves(BlockSyntax body, SemanticModel bodyModel)
    {
        var uses = new Dictionary<ISymbol, List<SyntaxNode>>(SymbolEqualityComparer.Default);
        var declarations = new List<(SyntaxNode Declaration, TypeSyntax Type)>();
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case IdentifierNameSyntax name when bodyModel.GetSymbol(name) is ILocalSymbol used:
                    if (!uses.TryGetValue(used, out var list))
                    {
                        list = [];
                        uses.Add(used, list);
                    }

                    list.Add(name);
                    break;
                // A constant has no value to move, and C# reads it where it is
                case VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax
                    {
                        Parent: LocalDeclarationStatementSyntax { IsConst: false }, Type: var type
                    }
                } declarator:
                    declarations.Add((declarator, type));
                    break;
                case DeclarationExpressionSyntax
                {
                    Parent: ArgumentSyntax, Designation: SingleVariableDesignationSyntax designation, Type: var type
                }:
                    declarations.Add((designation, type));
                    break;
            }
        }

        var plan = new MovePlan();
        foreach (var (declaration, type) in declarations)
        {
            if (bodyModel.GetSymbol(declaration) is not ILocalSymbol local ||
                !uses.TryGetValue(local, out var localUses) ||
                FindStatementInScope(declaration) is not { Parent: { } scope })
            {
                continue;
            }

            var target = scope;
            while (!localUses.AsStackEnumerable().All(ScopeOf(target), static (use, t) => IsInside(use, t)))
            {
                target = FindScopeAbove(target) ?? throw new InvalidOperationException(
                    $"G#: internal error: the uses of '{MessageText.Name(local)}' in a stack body lie outside the body " +
                    "(please report this G# bug).");
            }

            if (target == scope)
            {
                continue;
            }

            // [WHY] A ref local cannot be declared without a target and assigned later the same way
            if (type is RefTypeSyntax)
            {
                throw new InvalidOperationException(
                    $"G#: internal error: the ref local '{MessageText.Name(local)}' of a stack body lies outside the scope of its " +
                    "uses (please report this G# bug).");
            }

            ThrowIfNameTaken(ScopeOf(target), local, bodyModel);
            var move = new Move(declaration, local, type, FindAnchor(target, declaration, localUses));
            plan.ByDeclaration.Add(declaration, move);
            if (!plan.ByAnchor.TryGetValue(move.Anchor, out var atAnchor))
            {
                atAnchor = [];
                plan.ByAnchor.Add(move.Anchor, atAnchor);
            }

            atAnchor.Add(move);
        }

        return plan;
    }

    private static bool IsInside(SyntaxNode node, SyntaxNode scope) =>
        node.Ancestors()
            .AsStackEnumerable()
            .Any(scope, static (ancestor, s) => ancestor == s);

    private static bool IsScope(SyntaxNode node) => node is BlockSyntax or SwitchSectionSyntax;

    // The part of the body a local declared directly in <container> is in scope in: its block, or for a switch section
    // every section of the switch, which C# gives one scope
    private static SyntaxNode ScopeOf(SyntaxNode container) =>
        container is SwitchSectionSyntax { Parent: { } switchStatement } ? switchStatement : container;

    // The statement that holds the declaration directly in a block or switch section
    private static StatementSyntax? FindStatementInScope(SyntaxNode declaration)
    {
        for (var node = declaration; node is not null; node = node.Parent)
        {
            if (node is StatementSyntax statement && statement.Parent is { } parent && IsScope(parent))
            {
                return statement;
            }
        }

        return null;
    }

    private static SyntaxNode? FindScopeAbove(SyntaxNode scope)
    {
        for (var node = scope.Parent; node is not null; node = node.Parent)
        {
            if (IsScope(node))
            {
                return node;
            }
        }

        return null;
    }

    // The first statement of <target> that holds the declaration or a use: the moved declaration goes in front of it
    private static StatementSyntax FindAnchor(SyntaxNode target, SyntaxNode declaration, List<SyntaxNode> uses)
    {
        var statements = target switch
        {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            _ => throw new InvalidOperationException("A stack body local moves into a scope without statements.")
        };
        foreach (var statement in statements)
        {
            if (IsInside(declaration, statement) ||
                uses.AsStackEnumerable().Any(statement, static (use, s) => IsInside(use, s)))
            {
                return statement;
            }
        }

        throw new InvalidOperationException("A stack body local moves into a scope that does not hold it.");
    }

    // [WHY] C# gives a local the scope of its block; another local of the same name declared anywhere inside the scope
    // the local moves to would clash with the moved declaration (CS0136, CS0128)
    private static void ThrowIfNameTaken(SyntaxNode target, ILocalSymbol local, SemanticModel bodyModel)
    {
        foreach (var node in target.DescendantNodes())
        {
            var name = node switch
            {
                VariableDeclaratorSyntax declarator => declarator.Identifier.ValueText,
                SingleVariableDesignationSyntax designation => designation.Identifier.ValueText,
                _ => null
            };
            if (name == local.Name && !SymbolEqualityComparer.Default.Equals(bodyModel.GetSymbol(node), local))
            {
                throw new InvalidOperationException(
                    $"G#: internal error: the local '{MessageText.Name(local)}' of a stack body cannot move to the scope of its " +
                    "uses, which declares another local of the same name (please report this G# bug).");
            }
        }
    }

    // The type of a local written out: the declared type, or for var the type of the local, spelled in full and mapped
    // to the source of var so that the outputs find its symbol
    private static TypeSyntax WrittenType(TypeSyntax declared, ILocalSymbol local) =>
        declared is IdentifierNameSyntax { IsVar: true } && local.Type.Name != "var"
            ? SyntaxFactory.ParseTypeName(local.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .WithOriginalSpanFrom(declared)
            : declared.WithoutTrivia();

    // The name token of a declarator or of the designation of an out argument, as the source wrote it
    private static SyntaxToken DeclaredIdentifier(SyntaxNode declaration) => declaration switch
    {
        VariableDeclaratorSyntax declarator => declarator.Identifier.WithoutTrivia(),
        SingleVariableDesignationSyntax designation => designation.Identifier.WithoutTrivia(),
        _ => throw new InvalidOperationException("A moved local of a stack body has no name.")
    };

    // default(<type>), typed for the outputs through the source of the type. [WHY] scoped belongs to the local, not to
    // its type: default(scoped Span<T>) does not parse (CS1026), so the default names the type alone
    private static DefaultExpressionSyntax DefaultOf(TypeSyntax type)
    {
        var valueType = type is ScopedTypeSyntax { Type: var scopedType } ? scopedType : type;
        return SyntaxFactory.DefaultExpression(valueType).WithSpilledTypeSpanFrom(valueType);
    }

    // <type> <name> = default(<type>);
    private static LocalDeclarationStatementSyntax DeclareWithDefault(Move move)
    {
        var type = WrittenType(move.Type, move.Local);
        var declarator = SyntaxFactory.VariableDeclarator(DeclaredIdentifier(move.Declaration))
            .WithInitializer(SyntaxFactory.EqualsValueClause(DefaultOf(type)))
            .WithOriginalSpanFrom(move.Declaration);
        return SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(type, SyntaxFactory.SingletonSeparatedList(declarator))
        ).NormalizeWhitespace();
    }

    // Writes the moves: each moved local is declared with its default in front of its anchor; its old declarator becomes
    // an assignment of its value (or goes away without one), and its out argument names it
    private sealed class MoveRewriter(MovePlan plan) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitBlock(BlockSyntax node) =>
            node.WithStatements(RewriteStatements(node.Statements));

        public override SyntaxNode? VisitSwitchSection(SwitchSectionSyntax node) =>
            node.WithLabels(VisitList(node.Labels)).WithStatements(RewriteStatements(node.Statements));

        // out <type> <name> becomes out <name> when the local moves
        public override SyntaxNode? VisitDeclarationExpression(DeclarationExpressionSyntax node) =>
            node.Designation is SingleVariableDesignationSyntax designation &&
            plan.ByDeclaration.ContainsKey(designation)
                ? SyntaxFactory.IdentifierName(designation.Identifier.WithoutTrivia())
                    .WithOriginalSpanFrom(designation)
                    .WithTriviaFrom(node)
                : base.VisitDeclarationExpression(node);

        private SyntaxList<StatementSyntax> RewriteStatements(SyntaxList<StatementSyntax> statements)
        {
            var result = new List<StatementSyntax>(statements.Count);
            foreach (var statement in statements)
            {
                if (plan.ByAnchor.TryGetValue(statement, out var moves))
                {
                    foreach (var move in moves)
                    {
                        result.Add(DeclareWithDefault(move));
                    }
                }

                if (statement is LocalDeclarationStatementSyntax declaration &&
                    declaration.Declaration.Variables
                        .AsStackEnumerable()
                        .Any(plan, static (v, p) => p.ByDeclaration.ContainsKey(v)))
                {
                    AddWithoutMovedLocals(declaration, result);
                    continue;
                }

                result.Add(Visit(statement) as StatementSyntax ?? statement);
            }

            return SyntaxFactory.List(result);
        }

        // The declaration without its moved declarators; a moved one with a value becomes <name> = <value>;
        private void AddWithoutMovedLocals(LocalDeclarationStatementSyntax declaration, List<StatementSyntax> result)
        {
            var kept = new List<VariableDeclaratorSyntax>();
            var assignments = new List<StatementSyntax>();
            foreach (var variable in declaration.Declaration.Variables)
            {
                if (!plan.ByDeclaration.ContainsKey(variable))
                {
                    kept.Add(Visit(variable) as VariableDeclaratorSyntax ?? variable);
                    continue;
                }

                if (variable.Initializer is { Value: var value })
                {
                    // <name> = <value>;
                    var target = SyntaxFactory.IdentifierName(variable.Identifier.WithoutTrivia())
                        .WithOriginalSpanFrom(variable);
                    assignments.Add(SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            target,
                            Visit(value) as ExpressionSyntax ?? value
                        )
                    ));
                }
            }

            // [WHY] The declarators that stay are declared before the values of the moved ones, as C# evaluates a
            // declaration with several declarators from left to right; one that stays after a moved one with a value
            // would change that order
            if (kept.Count > 0 && assignments.Count > 0)
            {
                throw new InvalidOperationException(
                    "G#: internal error: a declaration of a stack body keeps some of its locals and moves others " +
                    "with a value (please report this G# bug).");
            }

            if (kept.Count > 0)
            {
                result.Add(declaration.WithDeclaration(
                    declaration.Declaration.WithVariables(SyntaxFactory.SeparatedList(kept))));
            }

            result.AddRange(assignments);
        }
    }

    // Gives every local declared without a value default(<type>), and var its type: the body runs as written, where C#
    // no longer sees that a local is assigned before it is read once the operands of && and || are statements
    private sealed class InitializerRewriter(SemanticModel bodyModel) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            var visited = base.VisitLocalDeclarationStatement(node) as LocalDeclarationStatementSyntax ?? node;
            var declaration = visited.Declaration;
            if (visited.IsConst || visited.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) ||
                declaration.Type is RefTypeSyntax ||
                declaration.Variables.AsStackEnumerable().All(static v => v.Initializer is not null))
            {
                return visited;
            }

            var type = declaration.Type;
            if (type is IdentifierNameSyntax { IsVar: true })
            {
                var first = declaration.Variables[0];
                type = bodyModel.GetSymbol(first) is ILocalSymbol local
                    ? WrittenType(type, local)
                    : throw new InvalidOperationException(
                        "G#: internal error: the type of an implicitly typed local of a stack body is not known " +
                        "(please report this G# bug).");
            }

            var variables = new List<VariableDeclaratorSyntax>(declaration.Variables.Count);
            foreach (var variable in declaration.Variables)
            {
                variables.Add(variable.Initializer is null
                    ? variable.WithInitializer(SyntaxFactory.EqualsValueClause(DefaultOf(type)))
                    : variable);
            }

            return visited.WithDeclaration(
                declaration.WithType(type.WithTrailingTrivia(SyntaxFactory.Space))
                    .WithVariables(SyntaxFactory.SeparatedList(variables))
            );
        }
    }
}
