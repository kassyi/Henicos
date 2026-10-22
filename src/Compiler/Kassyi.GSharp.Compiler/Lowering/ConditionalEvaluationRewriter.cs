namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Turns a short-circuit operator (<c>&amp;&amp;</c>, <c>||</c>) or a conditional operator (<c>?:</c>) whose
/// conditionally evaluated operand holds a warp primitive or a call that may suspend into an <c>if</c> statement.
/// </summary>
/// <remarks>
/// <see cref="WarpInstructionRewriter"/> hoists such calls in front of their statement, where they would run
/// unconditionally. After this pass the call sits in a branch that <see cref="BranchRewriter"/> lowers like any other.
/// <code>
/// bool ok = a &amp;&amp; S();   →   var t = a; if (t) { t = S(); } bool ok = t;
/// int v = c ? S() : x;   →   var t = default; if (c) { t = S(); } else { t = x; } int v = t;
/// </code>
/// The temporary of a conditional operator is initialized with <c>default</c> carrying the span of the original
/// operator, which is how <see cref="Transpiler.HoistedFieldTypeResolver"/> finds its type.
/// </remarks>
public class ConditionalEvaluationRewriter : CSharpSyntaxRewriter
{
    private int _tempCounter;

    /// <summary>Rewrites the conditional operators in the block that need it.</summary>
    public BlockSyntax Rewrite(BlockSyntax block) =>
        Visit(block) as BlockSyntax ?? throw new InvalidOperationException("Conditional evaluation lowering removed the block.");

    public override SyntaxNode VisitBlock(BlockSyntax node) => node.WithStatements(RewriteStatements(node.Statements));

    public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
    {
        var statements = RewriteStatements(node.Statements);

        // [WHY] A declaration directly under a case label is skipped by jumps to later labels, which C++ rejects
        return statements.Count == node.Statements.Count
            ? node.WithStatements(statements)
            : node.WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.Block(statements)));
    }

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        // An embedded statement without braces ('if (c) x = a && S();') has no list for the temporary
        if (node is StatementSyntax statement and not BlockSyntax &&
            statement.Parent is not (BlockSyntax or SwitchSectionSyntax or LabeledStatementSyntax) &&
            FindConditional(statement) != null)
        {
            return VisitBlock(SyntaxFactory.Block(statement));
        }

        return base.Visit(node);
    }

    // Lambdas and local functions are not part of the state machine (GSH0062)
    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;

    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;

    private SyntaxList<StatementSyntax> RewriteStatements(SyntaxList<StatementSyntax> statements)
    {
        var newStatements = ImmutableArray.CreateBuilder<StatementSyntax>(statements.Count);
        var pending = new Stack<StatementSyntax>();

        for (var i = statements.Count - 1; i >= 0; i--)
        {
            pending.Push(statements[i]);
        }

        // A rewritten statement goes back on the stack: its operands may hold further conditional operators
        while (pending.Count > 0)
        {
            var statement = pending.Pop();
            if (FindConditional(statement) is not { } conditional)
            {
                newStatements.Add(Visit(statement) as StatementSyntax
                                  ?? throw new InvalidOperationException("Conditional evaluation lowering removed a statement."));
                continue;
            }

            var (prefix, temp) = Lower(conditional);
            pending.Push(statement.ReplaceNode(conditional, temp.WithTriviaFrom(conditional)));
            for (var i = prefix.Length - 1; i >= 0; i--)
            {
                pending.Push(prefix[i]);
            }
        }

        return SyntaxFactory.List(newStatements);
    }

    private (ImmutableArray<StatementSyntax> Prefix, IdentifierNameSyntax Temp) Lower(ExpressionSyntax conditional)
    {
        var name = $"{GSharpInternalNames.ConditionTempPrefix}{_tempCounter++}";
        var temp = SyntaxFactory.IdentifierName(name);

        switch (conditional)
        {
            case BinaryExpressionSyntax binary:
                {
                    // var t = left; if (t) / if (!t) { t = right; }
                    ExpressionSyntax guard = binary.IsKind(SyntaxKind.LogicalAndExpression)
                        ? temp
                        : SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, temp);
                    return (ImmutableArray.Create<StatementSyntax>(
                        CreateTempDeclaration(name, binary.Left.WithoutTrivia()),
                        SyntaxFactory.IfStatement(guard, SyntaxFactory.Block(Assign(temp, binary.Right)))), temp);
                }
            case ConditionalExpressionSyntax ternary:
                {
                    // var t = default; if (c) { t = whenTrue; } else { t = whenFalse; }
                    var typedDefault = SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)
                        .WithAdditionalAnnotations(ternary.GetAnnotations(AstLoweringPipeline.OriginalSpanAnnotationKind));
                    return (ImmutableArray.Create<StatementSyntax>(
                        CreateTempDeclaration(name, typedDefault),
                        SyntaxFactory.IfStatement(
                            ternary.Condition.WithoutTrivia(),
                            SyntaxFactory.Block(Assign(temp, ternary.WhenTrue)),
                            SyntaxFactory.ElseClause(SyntaxFactory.Block(Assign(temp, ternary.WhenFalse))))), temp);
                }
            default:
                throw new InvalidOperationException($"Cannot lower a conditional expression of kind '{conditional.Kind()}'.");
        }
    }

    /// <summary>
    /// The first conditional operator, in evaluation order, of the statement's own expressions whose conditionally
    /// evaluated operand holds a warp primitive or a call that may suspend.
    /// </summary>
    /// <remarks>
    /// Nested statements, lambdas and loop headers are left out: nested statements have their own list, and a loop
    /// header is lowered after <see cref="LoopRewriter"/> has turned it into a statement of the flat list.
    /// </remarks>
    private static ExpressionSyntax? FindConditional(StatementSyntax statement)
    {
        if (statement is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
            or LabeledStatementSyntax)
        {
            return null;
        }

        foreach (var node in statement.DescendantNodes(child =>
                     child == statement ||
                     child is not (StatementSyntax or LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)))
        {
            switch (node)
            {
                case BinaryExpressionSyntax binary when
                    binary.IsKind(SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression) &&
                    ContainsWarpPrimitive(binary.Right):
                    return binary;
                case ConditionalExpressionSyntax ternary when
                    ContainsWarpPrimitive(ternary.WhenTrue) || ContainsWarpPrimitive(ternary.WhenFalse):
                    return ternary;
            }
        }

        return null;
    }

    private static bool ContainsWarpPrimitive(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            if (descendant is InvocationExpressionSyntax invocation &&
                (BarrierHelper.IsWarpShuffle(invocation) || BarrierHelper.IsSuspendingCall(invocation)))
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionStatementSyntax Assign(IdentifierNameSyntax target, ExpressionSyntax value) =>
        SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression, target, value.WithoutTrivia()));

    private static LocalDeclarationStatementSyntax CreateTempDeclaration(string name, ExpressionSyntax value) =>
        SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
                        .WithInitializer(SyntaxFactory.EqualsValueClause(value)))));
}
