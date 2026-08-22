using System.Collections.Frozen;
using Kassyi.GSharp.Transpiler.Ast;

namespace Kassyi.GSharp.Transpiler;

public class CudaGenerator : IAstVisitor
{
    private readonly CodeWriter _writer = new();

    public string Generate(BlockNode root, System.Collections.Generic.HashSet<string> variables)
    {
        if (variables != null)
        {
            foreach (var v in variables)
            {
                _writer.WriteLine($"int {v};");
            }
        }
        Visit(root);
        return _writer.ToString();
    }

    public void Visit(BlockNode node)
    {
        foreach (var stmt in node.Statements)
        {
            stmt.Accept(this);

            if (stmt is not (AssignNode or ArrayAssignNode or ForLoopNode or WhileNode or IfNode or BlockNode
                or LabelNode or GotoNode or IfGotoNode))
            {
                _writer.WriteLine(";");
            }
        }
    }

    public void Visit(ForLoopNode node)
    {
        _writer.Write("for ");
        using (_writer.Parens())
        {
            _writer.Write("int ").Write(node.IteratorName).Write(" = ");
            node.Start.Accept(this);
            _writer.Write("; ").Write(node.IteratorName).Write(" < ");
            node.End.Accept(this);
            _writer.Write("; ").Write(node.IteratorName).Write("++");
        }

        using (_writer.Block())
        {
            Visit(node.Body);
        }
    }

    public void Visit(WhileNode node)
    {
        _writer.Write("while ");
        using (_writer.Parens())
        {
            node.Condition.Accept(this);
        }

        using (_writer.Block())
        {
            Visit(node.Body);
        }
    }

    public void Visit(IfNode node)
    {
        _writer.Write("if ");
        using (_writer.Parens())
        {
            node.Condition.Accept(this);
        }

        using (_writer.Block())
        {
            Visit(node.TrueBlock);
        }

        _writer.Write("else");
        using (_writer.Block())
        {
            Visit(node.FalseBlock);
        }
    }

    public void Visit(AssignNode node)
    {
        _writer.Write(node.TargetName).Write(" = ");
        node.Value.Accept(this);
        _writer.WriteLine(";");
    }

    public void Visit(ArrayAssignNode node)
    {
        _writer.Write(node.ArrayName);
        using (_writer.Brackets())
        {
            node.Index.Accept(this);
        }
        _writer.Write(" = ");
        node.Value.Accept(this);
        _writer.WriteLine(";");
    }

    private static readonly FrozenDictionary<BinaryOperator, string> s_binOpStrs = new Dictionary<BinaryOperator, string>
    {
        { BinaryOperator.Add, " + " },
        { BinaryOperator.Sub, " - " },
        { BinaryOperator.Mul, " * " },
        { BinaryOperator.Div, " / " },
        { BinaryOperator.Mod, " % " },
        { BinaryOperator.BitAnd, " & " },
        { BinaryOperator.BitOr, " | " },
        { BinaryOperator.BitXor, " ^ " },
        { BinaryOperator.ShiftLeft, " << " },
        { BinaryOperator.ShiftRight, " >> " },
        { BinaryOperator.Eq, " == " },
        { BinaryOperator.Neq, " != " },
        { BinaryOperator.Lt, " < " },
        { BinaryOperator.Lte, " <= " },
        { BinaryOperator.Gt, " > " },
        { BinaryOperator.Gte, " >= " }
    }.ToFrozenDictionary();

    public void Visit(BinaryOpNode node)
    {
        using (_writer.Parens())
        {
            node.Left.Accept(this);

            if (!s_binOpStrs.TryGetValue(node.Op, out var opStr))
            {
                throw new NotImplementedException();
            }

            _writer.Write(opStr);
            node.Right.Accept(this);
        }
    }

    public void Visit(LiteralNode node) => _writer.Write(node.Value);

    public void Visit(VariableNode node) => _writer.Write(node.Name);

    public void Visit(ArrayAccessNode node)
    {
        _writer.Write(node.ArrayName);
        using (_writer.Brackets())
        {
            node.Index.Accept(this);
        }
    }

    public void Visit(MethodCallNode node)
    {
        _writer.Write(node.MethodName);
        using (_writer.Parens())
        {
            for (var i = 0; i < node.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    _writer.Write(", ");
                }
                node.Arguments[i].Accept(this);
            }
        }
    }

    public void Visit(LabelNode node) => _writer.WriteLine($"{node.Name}:");

    public void Visit(GotoNode node) => _writer.WriteLine($"goto {node.TargetName};");

    public void Visit(IfGotoNode node)
    {
        _writer.Write("if ");
        using (_writer.Parens())
        {
            node.Condition.Accept(this);
        }
        _writer.WriteLine($" goto {node.TrueTargetName};");
    }

    public void Visit(ReturnNode node)
    {
        if (node.Value != null)
        {
            _writer.Write("return ");
            node.Value.Accept(this);
            _writer.WriteLine(";");
        }
        else
        {
            _writer.WriteLine("return;");
        }
    }
}
