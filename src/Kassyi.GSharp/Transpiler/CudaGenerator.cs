using System;
using System.Text;
using System.Collections.Generic;
using Kassyi.GSharp.Transpiler.Ast;

namespace Kassyi.GSharp.Transpiler
{
    public class CudaGenerator : IAstVisitor
    {
        // 初期キャパシティを 4096 (4KB) 確保し、動的リサイズに伴うメモリアロケーションを防ぐ
        private StringBuilder _sb = new StringBuilder(4096);

        public string Generate(BlockNode root)
        {
            _sb.Clear();
            Visit(root);
            return _sb.ToString();
        }

        public void Visit(BlockNode node)
        {
            foreach (var stmt in node.Statements)
            {
                stmt.Accept(this);
                
                // If it's an expression node acting as a statement, append a semicolon.
                // Statements like AssignNode and ForLoopNode handle their own semicolons and newlines.
                if (stmt is not AssignNode and not ForLoopNode and not BlockNode)
                {
                    _sb.AppendLine(";");
                }
            }
        }

        public void Visit(ForLoopNode node)
        {
            _sb.Append("for (int ").Append(node.IteratorName).Append(" = ");
            node.Start.Accept(this);
            _sb.Append("; ").Append(node.IteratorName).Append(" < ");
            node.End.Accept(this);
            _sb.Append("; ").Append(node.IteratorName).AppendLine("++) {");
            
            Visit(node.Body);
            
            _sb.AppendLine("}");
        }

        public void Visit(AssignNode node)
        {
            _sb.Append(node.TargetName).Append(" = ");
            node.Value.Accept(this);
            _sb.AppendLine(";");
        }

        public void Visit(BinaryOpNode node)
        {
            _sb.Append('(');
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

            _sb.Append(opStr);
            node.Right.Accept(this);
            _sb.Append(')');
        }

        public void Visit(LiteralNode node)
        {
            _sb.Append(node.Value);
        }

        public void Visit(VariableNode node)
        {
            _sb.Append(node.Name);
        }

        public void Visit(MethodCallNode node)
        {
            _sb.Append(node.MethodName).Append('(');
            for (int i = 0; i < node.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    _sb.Append(", ");
                }
                node.Arguments[i].Accept(this);
            }
            _sb.Append(')');
        }
    }
}
