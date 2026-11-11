namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Lowers loop constructs (for, while, do-while) into flat goto sequences for linear state machine generation.</summary>
public class LoopRewriter : SplitPointBlockRewriter
{
    private int _labelCounter;
    private readonly Stack<(string? Break, string? Continue)> _loopStack = new();

    public override SyntaxNode VisitBreakStatement(BreakStatementSyntax node)
    {
        if (_loopStack.Count == 0 || _loopStack.Peek().Break is not { } breakLabel)
        {
            return node;
        }

        return LoweringSyntax.Goto(breakLabel);
    }

    public override SyntaxNode VisitContinueStatement(ContinueStatementSyntax node)
    {
        if (_loopStack.Count == 0 || _loopStack.Peek().Continue is not { } continueLabel)
        {
            return node;
        }

        return LoweringSyntax.Goto(continueLabel);
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        if (!BarrierHelper.ContainsPhaseBarrier(node))
        {
            return VisitBarrierFreeLoop(node, base.VisitWhileStatement);
        }

        return RewriteLoop("While", node.Condition, node.Statement, isPostCondition: false);
    }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    {
        if (!BarrierHelper.ContainsPhaseBarrier(node))
        {
            return VisitBarrierFreeLoop(node, base.VisitDoStatement);
        }

        return RewriteLoop("Do", node.Condition, node.Statement, isPostCondition: true);
    }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        if (!BarrierHelper.ContainsPhaseBarrier(node))
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

        statements.Add(LoweringSyntax.EmptyLabel(startLabel));

        if (!isPostCondition && condition != null)
        {
            // if (!(<condition>)) goto <end label>;
            var notCondition = SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.LogicalNotExpression,
                SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)Visit(condition))
            );
            statements.Add(SyntaxFactory.IfStatement(notCondition, LoweringSyntax.Goto(endLabel)));
        }

        AddInlined(statements, (StatementSyntax)Visit(bodySyntax));
        statements.Add(LoweringSyntax.EmptyLabel(continueLabel));

        if (incrementors != null)
        {
            statements.AddRange(incrementors);
        }

        if (isPostCondition && condition != null)
        {
            // if (<condition>) goto <start label>;
            var visitedCondition = (ExpressionSyntax)Visit(condition);
            statements.Add(SyntaxFactory.IfStatement(visitedCondition, LoweringSyntax.Goto(startLabel)));
        }
        else
        {
            statements.Add(LoweringSyntax.Goto(startLabel));
        }

        statements.Add(LoweringSyntax.EmptyLabel(endLabel));

        _loopStack.Pop();

        return SyntaxFactory.Block(statements);
    }
}
