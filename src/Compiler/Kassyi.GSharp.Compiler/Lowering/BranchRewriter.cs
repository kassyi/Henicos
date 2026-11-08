namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Lowers control flow branches (if/else, switch) that hold a split point into a flat sequence of statements using
/// conditional goto and labels.
/// </summary>
/// <param name="semanticModel">
/// Matches a <c>goto case</c> of a flattened switch to its case label by constant value
/// (<see cref="SwitchStatementFlattener"/>); without it, equivalent case expressions are matched.
/// </param>
public class BranchRewriter(SemanticModel? semanticModel = null) : CSharpSyntaxRewriter, IBlockLoweringPass
{
    private int _labelCounter;
    private int _switchCounter;

    private static bool ContainsBarrier(SyntaxNode node) =>
        node.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any(static descendant => descendant is InvocationExpressionSyntax inv && BarrierHelper.IsPhaseBarrier(inv));

    /// <summary>Rewrites branching statements within the block into flat goto sequences.</summary>
    public BlockSyntax Rewrite(BlockSyntax block) =>
        ContainsBarrier(block) ? (BlockSyntax)Visit(block) : block;

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = ImmutableArray.CreateBuilder<StatementSyntax>(node.Statements.Count);

        foreach (var statement in node.Statements)
        {
            var visited = (StatementSyntax)Visit(statement);

            if (visited is BlockSyntax nestedBlock)
            {
                // [WHY] Non-block statements (e.g. IfStatement) only become BlockSyntax when they already contain a barrier.
                // Explicit nested BlockSyntax nodes are only inlined if they contain a barrier to preserve variable scopes.
                if (statement is not BlockSyntax || ContainsBarrier(statement))
                {
                    newStatements.AddRange(nestedBlock.Statements);
                }
                else
                {
                    newStatements.Add(nestedBlock);
                }
            }
            else
            {
                newStatements.Add(visited);
            }
        }

        return SyntaxFactory.Block(newStatements);
    }

    // [WHY] A split point inside an unsafe block must reach the top level of the body, as one inside an if branch does;
    // the generated C++ and managed state machines are already emitted in an unsafe context.
    public override SyntaxNode? VisitUnsafeStatement(UnsafeStatementSyntax node) =>
        ContainsBarrier(node)
            ? VisitBlock(node.Block)
            : base.VisitUnsafeStatement(node);

    // [WHY] A split point inside a switch section must reach the top level of the body, as one inside an if branch does
    // (#708). The section statements are lowered first, so a nested if or switch is flattened into them.
    public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
    {
        if (!ContainsBarrier(node) || !SwitchStatementFlattener.CanFlatten(node))
        {
            return base.VisitSwitchStatement(node);
        }

        var id = _switchCounter++;
        var expression = (ExpressionSyntax)Visit(node.Expression);
        var sections = ImmutableArray.CreateBuilder<SyntaxList<StatementSyntax>>(node.Sections.Count);
        foreach (var section in node.Sections)
        {
            sections.Add(((BlockSyntax)VisitBlock(SyntaxFactory.Block(section.Statements))).Statements);
        }

        return SwitchStatementFlattener.Flatten(node, id, expression, sections.MoveToImmutable(), semanticModel);
    }

    public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
    {
        if (!ContainsBarrier(node))
        {
            return base.VisitIfStatement(node);
        }

        var id = _labelCounter++;
        var endLabelName = $"{GSharpInternalNames.Lowering.LabelPrefix}BranchEnd_{id}";
        var elseLabelName = $"{GSharpInternalNames.Lowering.LabelPrefix}BranchElse_{id}";

        // !(<condition>)
        var condition = (ExpressionSyntax)Visit(node.Condition);
        var notCondition = SyntaxFactory.PrefixUnaryExpression(
            SyntaxKind.LogicalNotExpression,
            SyntaxFactory.ParenthesizedExpression(condition));

        return node.Else is { } elseClause
            ? LowerIfElse(node, elseClause, notCondition, elseLabelName, endLabelName)
            : LowerIf(node, notCondition, endLabelName);
    }

    // { if (!(<condition>)) goto EndLabel; <then statements> EndLabel: ; }
    private BlockSyntax LowerIf(IfStatementSyntax node, ExpressionSyntax notCondition, string endLabelName)
    {
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        statements.Add(SyntaxFactory.IfStatement(notCondition, GotoLabel(endLabelName)));
        AddInlined(statements, (StatementSyntax)Visit(node.Statement));
        statements.Add(EmptyLabel(endLabelName));
        return SyntaxFactory.Block(statements);
    }

    // { if (!(<condition>)) goto ElseLabel; <then statements> goto EndLabel;
    //   ElseLabel: ; <else statements> EndLabel: ; }
    private BlockSyntax LowerIfElse(
        IfStatementSyntax node,
        ElseClauseSyntax elseClause,
        ExpressionSyntax notCondition,
        string elseLabelName,
        string endLabelName)
    {
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        statements.Add(SyntaxFactory.IfStatement(notCondition, GotoLabel(elseLabelName)));
        AddInlined(statements, (StatementSyntax)Visit(node.Statement));
        statements.Add(GotoLabel(endLabelName));

        // [WHY] We add the label to an empty statement and then inline the block to avoid creating an implicit local
        // block scope.
        var elseStmt = (StatementSyntax)Visit(elseClause.Statement);
        statements.Add(EmptyLabel(elseLabelName));
        AddInlined(statements, elseStmt);

        statements.Add(EmptyLabel(endLabelName));
        return SyntaxFactory.Block(statements);
    }

    // The statements of a block, or the statement itself
    private static void AddInlined(ImmutableArray<StatementSyntax>.Builder statements, StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            statements.AddRange(block.Statements);
        }
        else
        {
            statements.Add(statement);
        }
    }

    // goto <label>;
    private static GotoStatementSyntax GotoLabel(string label) =>
        SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(label));

    // <label>: ;
    private static LabeledStatementSyntax EmptyLabel(string label) =>
        SyntaxFactory.LabeledStatement(label, SyntaxFactory.EmptyStatement());
}
