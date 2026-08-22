using System;
using System.Text;
using System.Collections.Generic;
using Kassyi.GSharp.Transpiler.Ast;

namespace Kassyi.GSharp.Transpiler
{
    public class CudaGenerator : IAstVisitor
    {
        private CodeWriter _writer = new CodeWriter(4096);

        public string Generate(BlockNode root)
        {
            _writer = new CodeWriter(4096);
            Visit(root);
            return _writer.ToString();
        }

        public void Visit(BlockNode node)
        {
            foreach (var stmt in node.Statements)
            {
                if (stmt is not BlockNode) _writer.WriteIndent();
                
                stmt.Accept(this);
                
                if (stmt is not AssignNode and not ArrayAssignNode and not ForLoopNode and not WhileNode and not IfNode and not BlockNode)
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
            
            if (node.FalseBlock != null)
            {
                _writer.WriteIndent().Write("else");
                using (_writer.Block())
                {
                    Visit(node.FalseBlock);
                }
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

        public void Visit(BinaryOpNode node)
        {
            using (_writer.Parens())
            {
                node.Left.Accept(this);
                
                string opStr = node.Op switch
                {
                    BinaryOperator.Add => " + ",
                    BinaryOperator.Sub => " - ",
                    BinaryOperator.Mul => " * ",
                    BinaryOperator.Div => " / ",
                    BinaryOperator.Mod => " % ",
                    BinaryOperator.BitAnd => " & ",
                    BinaryOperator.BitOr => " | ",
                    BinaryOperator.BitXor => " ^ ",
                    BinaryOperator.ShiftLeft => " << ",
                    BinaryOperator.ShiftRight => " >> ",
                    BinaryOperator.Eq => " == ",
                    BinaryOperator.Neq => " != ",
                    BinaryOperator.Lt => " < ",
                    BinaryOperator.Lte => " <= ",
                    BinaryOperator.Gt => " > ",
                    BinaryOperator.Gte => " >= ",
                    _ => throw new NotImplementedException()
                };

                _writer.Write(opStr);
                node.Right.Accept(this);
            }
        }

        public void Visit(LiteralNode node)
        {
            _writer.Write(node.Value);
        }

        public void Visit(VariableNode node)
        {
            _writer.Write(node.Name);
        }

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
                for (int i = 0; i < node.Arguments.Count; i++)
                {
                    if (i > 0)
                    {
                        _writer.Write(", ");
                    }
                    node.Arguments[i].Accept(this);
                }
            }
        }
    }
}
