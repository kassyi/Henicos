namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Phase 2.4: Lowers loop constructs (for, while, do-while, break, continue) into flat goto sequences.
/// </summary>
public class LoopRewriter : CSharpSyntaxRewriter
{
    private int _labelCounter;
    private readonly Stack<(string Break, string Continue)> _loopStack = new();

    private static bool ContainsBarrier(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            if (descendant is not InvocationExpressionSyntax inv)
            {
                continue;
            }

            if ((inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SyncThreads" } ma &&
                 (ma.Expression as IdentifierNameSyntax)?.Identifier.ValueText == "Gpu") ||
                inv.Expression is IdentifierNameSyntax { Identifier.ValueText: "SyncThreads" })
            {
                return true;
            }
        }

        return false;
    }

    public BlockSyntax Rewrite(BlockSyntax block) => (BlockSyntax)Visit(block);

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = ImmutableArray.CreateBuilder<StatementSyntax>();

        foreach (var statement in node.Statements)
        {
            var visited = (StatementSyntax)Visit(statement);

            if (visited is BlockSyntax nestedBlock)
            {
                if (ContainsBarrier(statement))
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

    public override SyntaxNode VisitBreakStatement(BreakStatementSyntax node)
    {
        if (_loopStack.Count == 0)
        {
            return node;
        }

        var (breakLabel, _) = _loopStack.Peek();
        return SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(breakLabel));
    }

    public override SyntaxNode VisitContinueStatement(ContinueStatementSyntax node)
    {
        if (_loopStack.Count == 0)
        {
            return node;
        }

        var (_, continueLabel) = _loopStack.Peek();
        return SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(continueLabel));
    }

    public override SyntaxNode VisitWhileStatement(WhileStatementSyntax node)
    {
        if (!ContainsBarrier(node))
        {
            return base.VisitWhileStatement(node)!;
        }

        return RewriteLoop("While", node.Condition, node.Statement, isPostCondition: false);
    }

    public override SyntaxNode VisitDoStatement(DoStatementSyntax node)
    {
        if (!ContainsBarrier(node))
        {
            return base.VisitDoStatement(node)!;
        }

        return RewriteLoop("Do", node.Condition, node.Statement, isPostCondition: true);
    }

    public override SyntaxNode VisitForStatement(ForStatementSyntax node)
    {
        if (!ContainsBarrier(node))
        {
            return base.VisitForStatement(node)!;
        }

        var initializers = ImmutableArray.CreateBuilder<StatementSyntax>();
        if (node.Declaration != null)
        {
            initializers.Add(
                SyntaxFactory.LocalDeclarationStatement((VariableDeclarationSyntax)Visit(node.Declaration)));
        }

        foreach (var init in node.Initializers)
        {
            initializers.Add(SyntaxFactory.ExpressionStatement((ExpressionSyntax)Visit(init)));
        }

        var incrementors = ImmutableArray.CreateBuilder<StatementSyntax>();
        foreach (var inc in node.Incrementors)
        {
            incrementors.Add(SyntaxFactory.ExpressionStatement((ExpressionSyntax)Visit(inc)));
        }

        return RewriteLoop(
            "For",
            node.Condition,
            node.Statement,
            isPostCondition: false,
            initializers: initializers.Count > 0 ? initializers : null,
            incrementors: incrementors.Count > 0 ? incrementors : null);
    }

    /// <summary>
    /// Flattens a loop construct into a linear sequence of statements using labels and gotos.
    /// By centralizing the control flow flattening, we guarantee consistent behavior for continue/break stack 
    /// tracking across all loop types (while, do-while, for) without redundant AST manipulations.
    /// </summary>
    private BlockSyntax RewriteLoop(
        string prefix,
        ExpressionSyntax? condition,
        StatementSyntax bodySyntax,
        bool isPostCondition,
        IEnumerable<StatementSyntax>? initializers = null,
        IEnumerable<StatementSyntax>? incrementors = null)
    {
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        var id = _labelCounter++;
        var startLabel = $"_{prefix}Start_{id}";
        var continueLabel = $"_{prefix}Continue_{id}";
        var endLabel = $"_{prefix}End_{id}";

        if (initializers != null)
        {
            statements.AddRange(initializers);
        }

        _loopStack.Push((endLabel, continueLabel));

        statements.Add(SyntaxFactory.LabeledStatement(startLabel, SyntaxFactory.EmptyStatement()));

        if (!isPostCondition && condition != null)
        {
            var notCondition = SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.LogicalNotExpression,
                SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)Visit(condition)));
            var gotoEnd = SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(endLabel));
            statements.Add(SyntaxFactory.IfStatement(notCondition, gotoEnd));
        }

        var body = (StatementSyntax)Visit(bodySyntax);
        if (body is BlockSyntax bodyBlock)
        {
            statements.AddRange(bodyBlock.Statements);
        }
        else
        {
            statements.Add(body);
        }

        statements.Add(SyntaxFactory.LabeledStatement(continueLabel, SyntaxFactory.EmptyStatement()));

        if (incrementors != null)
        {
            statements.AddRange(incrementors);
        }

        if (isPostCondition && condition != null)
        {
            var visitedCondition = (ExpressionSyntax)Visit(condition);
            var gotoStart =
                SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(startLabel));
            statements.Add(SyntaxFactory.IfStatement(visitedCondition, gotoStart));
        }
        else
        {
            statements.Add(SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement,
                SyntaxFactory.IdentifierName(startLabel)));
        }

        statements.Add(SyntaxFactory.LabeledStatement(endLabel, SyntaxFactory.EmptyStatement()));

        _loopStack.Pop();

        return SyntaxFactory.Block(statements);
    }
}
