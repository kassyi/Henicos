namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Lowers loop constructs (for, while, do-while) into flat goto sequences for linear state machine generation.</summary>
public class LoopRewriter : CSharpSyntaxRewriter
{
    private int _labelCounter;
    private readonly Stack<(string? Break, string? Continue)> _loopStack = new();

    private static bool ContainsBarrier(SyntaxNode node) =>
        node.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any(static descendant => descendant is InvocationExpressionSyntax inv && BarrierHelper.IsPhaseBarrier(inv));

    /// <summary>Rewrites loops within the block into flat goto sequences.</summary>
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
                // [WHY] Loop statements only become BlockSyntax when they already contain a barrier.
                // Explicit nested BlockSyntax nodes are only inlined if they contain a barrier.
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

    public override SyntaxNode VisitBreakStatement(BreakStatementSyntax node)
    {
        if (_loopStack.Count == 0 || _loopStack.Peek().Break is not { } breakLabel)
        {
            return node;
        }

        return SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(breakLabel));
    }

    public override SyntaxNode VisitContinueStatement(ContinueStatementSyntax node)
    {
        if (_loopStack.Count == 0 || _loopStack.Peek().Continue is not { } continueLabel)
        {
            return node;
        }

        return SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(continueLabel));
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        if (!ContainsBarrier(node))
        {
            return VisitBarrierFreeLoop(node, base.VisitWhileStatement);
        }

        return RewriteLoop("While", node.Condition, node.Statement, isPostCondition: false);
    }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    {
        if (!ContainsBarrier(node))
        {
            return VisitBarrierFreeLoop(node, base.VisitDoStatement);
        }

        return RewriteLoop("Do", node.Condition, node.Statement, isPostCondition: true);
    }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        if (!ContainsBarrier(node))
        {
            return VisitBarrierFreeLoop(node, base.VisitForStatement);
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

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node) =>
        VisitBarrierFreeLoop(node, base.VisitForEachStatement);

    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node) =>
        VisitBarrierFreeLoop(node, base.VisitForEachVariableStatement);

    public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
    {
        var currentContinue = _loopStack.Count > 0 ? _loopStack.Peek().Continue : null;
        _loopStack.Push((null, currentContinue));
        try
        {
            return base.VisitSwitchStatement(node);
        }
        finally
        {
            _loopStack.Pop();
        }
    }

    private SyntaxNode? VisitBarrierFreeLoop<TNode>(TNode node, Func<TNode, SyntaxNode?> visitBase)
        where TNode : SyntaxNode
    {
        _loopStack.Push((null, null));
        try
        {
            return visitBase(node);
        }
        finally
        {
            _loopStack.Pop();
        }
    }

    /// <summary>Flattens a loop construct into a linear sequence of statements using labels and gotos.</summary>
    private BlockSyntax RewriteLoop(
        string prefix,
        ExpressionSyntax? condition,
        StatementSyntax bodySyntax,
        bool isPostCondition,
        IEnumerable<StatementSyntax>? initializers = null,
        IEnumerable<StatementSyntax>? incrementors = null)
    {
        // [WHY] Centralizing control flow flattening ensures consistent break/continue stack tracking across all loop types.
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        var id = _labelCounter++;
        var startLabel = $"{GSharpInternalNames.Lowering.LabelPrefix}{prefix}Start_{id}";
        var continueLabel = $"{GSharpInternalNames.Lowering.LabelPrefix}{prefix}Continue_{id}";
        var endLabel = $"{GSharpInternalNames.Lowering.LabelPrefix}{prefix}End_{id}";

        if (initializers != null)
        {
            statements.AddRange(initializers);
        }

        _loopStack.Push((endLabel, continueLabel));

        statements.Add(SyntaxFactory.LabeledStatement(startLabel, SyntaxFactory.EmptyStatement()));

        if (!isPostCondition && condition != null)
        {
            // if (!(<condition>)) goto <end label>;
            var notCondition = SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.LogicalNotExpression,
                SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)Visit(condition))
            );
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
