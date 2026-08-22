namespace Kassyi.GSharp.Transpiler.Ast;

public class AstRenamerVisitor(
    string prefix,
    string endLabel,
    string? returnVar,
    AstContext targetContext,
    AstContext? sourceContext = null)
    : IAstVisitor
{
    private readonly List<IAstNode> _newStatements = [];

    public IReadOnlyList<IAstNode> TransformedStatements => _newStatements;

    private string Rename(string name)
    {
        var newName = prefix + name;
        if (sourceContext != null && sourceContext.LocalVariables.TryGetValue(name, out var type))
        {
            targetContext.LocalVariables[newName] = type;
        }

        return newName;
    }

    public void Visit(BlockNode node)
    {
        foreach (var stmt in node.Statements)
        {
            stmt.Accept(this);
        }
    }

    public void Visit(ForLoopNode node)
    {
        var it = Rename(node.IteratorName);
        var start = VisitAndGet(node.Start);
        var end = VisitAndGet(node.End);

        var bodyVisitor = new AstRenamerVisitor(prefix, endLabel, returnVar, targetContext, sourceContext);
        bodyVisitor.Visit(node.Body);
        var newBody = new BlockNode(bodyVisitor.TransformedStatements);

        _newStatements.Add(new ForLoopNode(it, start, end, newBody));
    }

    public void Visit(WhileNode node)
    {
        var cond = VisitAndGet(node.Condition);

        var bodyVisitor = new AstRenamerVisitor(prefix, endLabel, returnVar, targetContext, sourceContext);
        bodyVisitor.Visit(node.Body);
        var newBody = new BlockNode(bodyVisitor.TransformedStatements);

        _newStatements.Add(new WhileNode(cond, newBody));
    }

    public void Visit(IfNode node)
    {
        var cond = VisitAndGet(node.Condition);

        var trueVisitor = new AstRenamerVisitor(prefix, endLabel, returnVar, targetContext, sourceContext);
        trueVisitor.Visit(node.TrueBlock);
        var newTrue = new BlockNode(trueVisitor.TransformedStatements);

        BlockNode? newFalse = null;
        if (node.FalseBlock != null)
        {
            var falseVisitor = new AstRenamerVisitor(prefix, endLabel, returnVar, targetContext, sourceContext);
            falseVisitor.Visit(node.FalseBlock);
            newFalse = new BlockNode(falseVisitor.TransformedStatements);
        }

        _newStatements.Add(new IfNode(cond, newTrue, newFalse));
    }

    public void Visit(AssignNode node) =>
        _newStatements.Add(new AssignNode(Rename(node.TargetName), VisitAndGet(node.Value)));

    public void Visit(ArrayAssignNode node) => _newStatements.Add(new ArrayAssignNode(Rename(node.ArrayName),
        VisitAndGet(node.Index), VisitAndGet(node.Value)));

    public void Visit(BinaryOpNode node) =>
        _newStatements.Add(new BinaryOpNode(node.Op, VisitAndGet(node.Left), VisitAndGet(node.Right)));

    public void Visit(LiteralNode node) => _newStatements.Add(node);

    public void Visit(VariableNode node) => _newStatements.Add(new VariableNode(Rename(node.Name)));

    public void Visit(ArrayAccessNode node) =>
        _newStatements.Add(new ArrayAccessNode(Rename(node.ArrayName), VisitAndGet(node.Index)));

    public void Visit(MethodCallNode node)
    {
        var newArgs = node.Arguments.Select(VisitAndGet).ToList();
        _newStatements.Add(node with { Arguments = newArgs });
    }

    public void Visit(LabelNode node) => _newStatements.Add(new LabelNode(prefix + node.Name));

    public void Visit(GotoNode node) => _newStatements.Add(new GotoNode(prefix + node.TargetName));

    public void Visit(IfGotoNode node) =>
        _newStatements.Add(new IfGotoNode(VisitAndGet(node.Condition), prefix + node.TrueTargetName));

    public void Visit(SwitchNode node)
    {
        var newLabels = node.CaseLabels.Select(label => prefix + label).ToArray();
        _newStatements.Add(new SwitchNode(VisitAndGet(node.Value), newLabels));
    }

    public void Visit(ReturnNode node)
    {
        if (node.Value != null && returnVar != null)
        {
            _newStatements.Add(new AssignNode(returnVar, VisitAndGet(node.Value)));
        }

        _newStatements.Add(new GotoNode(endLabel));
    }

    public void Visit(AddressOfNode node) => _newStatements.Add(new AddressOfNode(VisitAndGet(node.Target)));

    public void Visit(DereferenceNode node) => _newStatements.Add(new DereferenceNode(VisitAndGet(node.Pointer)));

    public void Visit(DereferenceAssignNode node) =>
        _newStatements.Add(new DereferenceAssignNode(VisitAndGet(node.Pointer), VisitAndGet(node.Value)));

    public void Visit(ExpressionStatementNode node) =>
        _newStatements.Add(new ExpressionStatementNode(VisitAndGet(node.Expression)));

    public void Visit(FieldAccessNode node) => _newStatements.Add(node with { Target = VisitAndGet(node.Target) });

    public void Visit(FieldAssignNode node) =>
        _newStatements.Add(node with { Target = VisitAndGet(node.Target), Value = VisitAndGet(node.Value) });

    public void Visit(FieldAddressNode node) => _newStatements.Add(node with { Target = VisitAndGet(node.Target) });

    public void Visit(IntrinsicNode node)
    {
        var newArgs = node.Arguments.Select(VisitAndGet).ToList();
        _newStatements.Add(node with { Arguments = newArgs });
    }

    private IAstNode VisitAndGet(IAstNode node)
    {
        var subVisitor = new AstRenamerVisitor(prefix, endLabel, returnVar, targetContext, sourceContext);
        node.Accept(subVisitor);
        return subVisitor.TransformedStatements[0];
    }
}
