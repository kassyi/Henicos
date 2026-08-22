using Kassyi.GSharp.Transpiler.Ast;
using System.Collections.Generic;
using System.Linq;

namespace Kassyi.GSharp.Transpiler;

public class AstRenamerVisitor : IAstVisitor
{
    private readonly string _prefix;
    private readonly string _endLabel;
    private readonly string _returnVar;
    private readonly AstContext _context;
    
    private readonly List<IAstNode> _newStatements = new();
    
    public IReadOnlyList<IAstNode> TransformedStatements => _newStatements;

    public AstRenamerVisitor(string prefix, string endLabel, string returnVar, AstContext context)
    {
        _prefix = prefix;
        _endLabel = endLabel;
        _returnVar = returnVar;
        _context = context;
    }

    private string Rename(string name)
    {
        var newName = _prefix + name;
        _context.LocalVariables.Add(newName);
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
        
        var bodyVisitor = new AstRenamerVisitor(_prefix, _endLabel, _returnVar, _context);
        bodyVisitor.Visit(node.Body);
        var newBody = new BlockNode(bodyVisitor.TransformedStatements);
        
        _newStatements.Add(new ForLoopNode(it, start, end, newBody));
    }

    public void Visit(WhileNode node)
    {
        var cond = VisitAndGet(node.Condition);
        
        var bodyVisitor = new AstRenamerVisitor(_prefix, _endLabel, _returnVar, _context);
        bodyVisitor.Visit(node.Body);
        var newBody = new BlockNode(bodyVisitor.TransformedStatements);
        
        _newStatements.Add(new WhileNode(cond, newBody));
    }

    public void Visit(IfNode node)
    {
        var cond = VisitAndGet(node.Condition);
        
        var trueVisitor = new AstRenamerVisitor(_prefix, _endLabel, _returnVar, _context);
        trueVisitor.Visit(node.TrueBlock);
        var newTrue = new BlockNode(trueVisitor.TransformedStatements);
        
        BlockNode newFalse = null;
        if (node.FalseBlock != null)
        {
            var falseVisitor = new AstRenamerVisitor(_prefix, _endLabel, _returnVar, _context);
            falseVisitor.Visit(node.FalseBlock);
            newFalse = new BlockNode(falseVisitor.TransformedStatements);
        }
        
        _newStatements.Add(new IfNode(cond, newTrue, newFalse));
    }

    public void Visit(AssignNode node)
    {
        _newStatements.Add(new AssignNode(Rename(node.TargetName), VisitAndGet(node.Value)));
    }

    public void Visit(ArrayAssignNode node)
    {
        _newStatements.Add(new ArrayAssignNode(Rename(node.ArrayName), VisitAndGet(node.Index), VisitAndGet(node.Value)));
    }

    public void Visit(BinaryOpNode node)
    {
        _newStatements.Add(new BinaryOpNode(node.Op, VisitAndGet(node.Left), VisitAndGet(node.Right)));
    }

    public void Visit(LiteralNode node)
    {
        _newStatements.Add(node);
    }

    public void Visit(VariableNode node)
    {
        _newStatements.Add(new VariableNode(Rename(node.Name)));
    }

    public void Visit(ArrayAccessNode node)
    {
        _newStatements.Add(new ArrayAccessNode(Rename(node.ArrayName), VisitAndGet(node.Index)));
    }

    public void Visit(MethodCallNode node)
    {
        var newArgs = node.Arguments.Select(VisitAndGet).ToList();
        _newStatements.Add(new MethodCallNode(node.MethodName, newArgs));
    }

    public void Visit(LabelNode node)
    {
        _newStatements.Add(new LabelNode(_prefix + node.Name));
    }

    public void Visit(GotoNode node)
    {
        _newStatements.Add(new GotoNode(_prefix + node.TargetName));
    }

    public void Visit(IfGotoNode node)
    {
        _newStatements.Add(new IfGotoNode(VisitAndGet(node.Condition), _prefix + node.TrueTargetName));
    }

    public void Visit(ReturnNode node)
    {
        if (node.Value != null && _returnVar != null)
        {
            _newStatements.Add(new AssignNode(_returnVar, VisitAndGet(node.Value)));
        }
        _newStatements.Add(new GotoNode(_endLabel));
    }
    
    private IAstNode VisitAndGet(IAstNode node)
    {
        if (node == null) return null;
        var subVisitor = new AstRenamerVisitor(_prefix, _endLabel, _returnVar, _context);
        node.Accept(subVisitor);
        return subVisitor.TransformedStatements[0];
    }
}
