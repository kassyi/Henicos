namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Lowers control flow branches (if/else) into a flat sequence of statements using conditional goto and labels.
/// This guarantees that StateMachineLowerer can process SyncThreads() linearly.
/// </summary>
public class BranchRewriter : CSharpSyntaxRewriter
{
    private int _labelCounter;

    public BlockSyntax Rewrite(BlockSyntax block) => (BlockSyntax)Visit(block);

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = ImmutableArray.CreateBuilder<StatementSyntax>();

        foreach (var statement in node.Statements)
        {
            var visited = (StatementSyntax)Visit(statement);

            if (visited is BlockSyntax nestedBlock)
            {
                // Inline nested blocks to ensure a completely flat statement list for the StateMachineLowerer.
                // C# prevents local variable shadowing within the same method (CS0136), 
                // so flattening scopes will not cause name collisions.
                newStatements.AddRange(nestedBlock.Statements);
            }
            else
            {
                newStatements.Add(visited);
            }
        }

        return SyntaxFactory.Block(newStatements);
    }

    public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
    {
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        var id = _labelCounter++;
        var endLabelName = $"_BranchEnd_{id}";
        var elseLabelName = $"_BranchElse_{id}";

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
                // We add the label to an empty statement and then inline the block.
                // This prevents LabeledStatement from creating an implicit local block scope.
                statements.Add(SyntaxFactory.LabeledStatement(elseLabelName, SyntaxFactory.EmptyStatement()));
                statements.AddRange(elseBlock.Statements);
            }
            else
            {
                statements.Add(SyntaxFactory.LabeledStatement(elseLabelName, elseStmt));
            }

            // EndLabel:
            var endLabel = SyntaxFactory.LabeledStatement(endLabelName, SyntaxFactory.EmptyStatement());
            statements.Add(endLabel);
        }

        return SyntaxFactory.Block(statements);
    }
}
