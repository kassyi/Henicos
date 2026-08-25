namespace Kassyi.GSharp.Compiler.Transpiler;

public partial class RoslynToCudaVisitor
{
    public override void VisitBlock(BlockSyntax node)
    {
        foreach (var statement in node.Statements)
        {
            Visit(statement);
            Writer.AppendLine();
        }
    }

    public override void VisitIfStatement(IfStatementSyntax node)
        => StatementTranspilers.TranspileIfStatement(node, this);

    public override void VisitFixedStatement(FixedStatementSyntax node)
    {
        Visit(node.Declaration);
        Writer.AppendSemiColon();
        Writer.AppendLine();
        VisitStatementOrBlock(node.Statement);
    }

    public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
        => VisitStatementOrBlock(node.Block);

    public override void VisitForStatement(ForStatementSyntax node)
        => StatementTranspilers.TranspileForStatement(node, this);

    public override void VisitWhileStatement(WhileStatementSyntax node)
        => StatementTranspilers.TranspileWhileStatement(node, this);

    public override void VisitDoStatement(DoStatementSyntax node)
        => StatementTranspilers.TranspileDoStatement(node, this);

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
        => StatementTranspilers.TranspileSwitchStatement(node, this);

    public override void VisitSwitchSection(SwitchSectionSyntax node)
        => StatementTranspilers.TranspileSwitchSection(node, this);

    public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        Writer.AppendKeyword("case");
        Writer.AppendSpace();
        Visit(node.Value);
        Writer.Append(":");
    }

    public override void VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node) => Writer.Append("default:");

    public override void VisitBreakStatement(BreakStatementSyntax node)
        => StatementTranspilers.TranspileBreakStatement(node, this);

    public override void VisitContinueStatement(ContinueStatementSyntax node)
        => StatementTranspilers.TranspileContinueStatement(node, this);

    public override void VisitReturnStatement(ReturnStatementSyntax node)
        => StatementTranspilers.TranspileReturnStatement(node, this);

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        => DeclarationTranspilers.TranspileLocalDeclarationStatement(node, this);

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        => DeclarationTranspilers.TranspileVariableDeclaration(node, this);
}
