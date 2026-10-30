namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Lowers control flow branches (if/else, switch) that hold a split point into a flat sequence of statements using
/// conditional goto and labels.
/// </summary>
/// <param name="semanticModel">
/// Matches a <c>goto case</c> of a flattened switch to its case label by constant value
/// (<see cref="SwitchStatementFlattener"/>); without it, equivalent case expressions are matched.
/// </param>
public class BranchRewriter(SemanticModel? semanticModel = null) : CSharpSyntaxRewriter
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

        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        var id = _labelCounter++;
        var endLabelName = $"{GSharpInternalNames.Lowering.LabelPrefix}BranchEnd_{id}";
        var elseLabelName = $"{GSharpInternalNames.Lowering.LabelPrefix}BranchElse_{id}";

        var condition = (ExpressionSyntax)Visit(node.Condition);
        var notCondition = SyntaxFactory.PrefixUnaryExpression(
            SyntaxKind.LogicalNotExpression,
            SyntaxFactory.ParenthesizedExpression(condition));

        if (node.Else == null)
        {
            // if (!condition) goto EndLabel;
            var gotoEnd = SyntaxFactory.GotoStatement(
                SyntaxKind.GotoStatement,
                SyntaxFactory.IdentifierName(endLabelName));

            var ifGoto = SyntaxFactory.IfStatement(notCondition, gotoEnd);
            statements.Add(ifGoto);

            var trueStmt = (StatementSyntax)Visit(node.Statement);
            if (trueStmt is BlockSyntax trueBlock)
            {
                statements.AddRange(trueBlock.Statements);
            }
            else
            {
                statements.Add(trueStmt);
            }

            // EndLabel:
            var endLabel = SyntaxFactory.LabeledStatement(endLabelName, SyntaxFactory.EmptyStatement());
            statements.Add(endLabel);
        }
        else
        {
            // if (!condition) goto ElseLabel;
            var gotoElse = SyntaxFactory.GotoStatement(
                SyntaxKind.GotoStatement,
                SyntaxFactory.IdentifierName(elseLabelName));

            var ifGoto = SyntaxFactory.IfStatement(notCondition, gotoElse);
            statements.Add(ifGoto);

            var trueStmt = (StatementSyntax)Visit(node.Statement);
            if (trueStmt is BlockSyntax trueBlock)
            {
                statements.AddRange(trueBlock.Statements);
            }
            else
            {
                statements.Add(trueStmt);
            }

            // goto EndLabel;
            var gotoEnd = SyntaxFactory.GotoStatement(
                SyntaxKind.GotoStatement,
                SyntaxFactory.IdentifierName(endLabelName));
            statements.Add(gotoEnd);

            // ElseLabel:
            var elseStmt = (StatementSyntax)Visit(node.Else.Statement);
            if (elseStmt is BlockSyntax elseBlock)
            {
                // [WHY] We add the label to an empty statement and then inline the block to avoid creating an implicit local block scope.
                statements.Add(SyntaxFactory.LabeledStatement(elseLabelName, SyntaxFactory.EmptyStatement()));
                statements.AddRange(elseBlock.Statements);
            }
            else
            {
                statements.Add(SyntaxFactory.LabeledStatement(elseLabelName, SyntaxFactory.EmptyStatement()));
                statements.Add(elseStmt);
            }

            // EndLabel:
            var endLabel = SyntaxFactory.LabeledStatement(endLabelName, SyntaxFactory.EmptyStatement());
            statements.Add(endLabel);
        }

        return SyntaxFactory.Block(statements);
    }
}
