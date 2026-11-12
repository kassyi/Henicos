namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Lowers control flow branches (if/else, switch) that hold a split point into a flat sequence of statements using
/// conditional goto and labels.
/// </summary>
/// <param name="semanticModel">
/// Matches a <c>goto case</c> of a flattened switch to its case label by constant value
/// (<see cref="SwitchStatementFlattener"/>); without it, equivalent case expressions are matched.
/// </param>
public class BranchRewriter(SemanticModel? semanticModel = null) : SplitPointBlockRewriter
{
    private int _labelCounter;
    private int _switchCounter;

    // [WHY] A split point inside an unsafe block must reach the top level of the body, as one inside an if branch does;
    // the generated C++ and managed state machines are already emitted in an unsafe context.
    public override SyntaxNode? VisitUnsafeStatement(UnsafeStatementSyntax node) =>
        BarrierHelper.ContainsPhaseBarrier(node)
            ? VisitBlock(node.Block)
            : base.VisitUnsafeStatement(node);

    // [WHY] A split point inside a switch section must reach the top level of the body, as one inside an if branch does
    // (#708). The section statements are lowered first, so a nested if or switch is flattened into them.
    public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
    {
        if (!BarrierHelper.ContainsPhaseBarrier(node) || !SwitchStatementFlattener.CanFlatten(node))
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
        if (!BarrierHelper.ContainsPhaseBarrier(node))
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
        statements.Add(SyntaxFactory.IfStatement(notCondition, LoweringSyntax.Goto(endLabelName)));
        AddInlined(statements, (StatementSyntax)Visit(node.Statement));
        statements.Add(LoweringSyntax.EmptyLabel(endLabelName));
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
        statements.Add(SyntaxFactory.IfStatement(notCondition, LoweringSyntax.Goto(elseLabelName)));
        AddInlined(statements, (StatementSyntax)Visit(node.Statement));
        statements.Add(LoweringSyntax.Goto(endLabelName));

        // [WHY] We add the label to an empty statement and then inline the block to avoid creating an implicit local
        // block scope.
        var elseStmt = (StatementSyntax)Visit(elseClause.Statement);
        statements.Add(LoweringSyntax.EmptyLabel(elseLabelName));
        AddInlined(statements, elseStmt);

        statements.Add(LoweringSyntax.EmptyLabel(endLabelName));
        return SyntaxFactory.Block(statements);
    }
}
