using System;
using System.Text;
using System.Collections.Generic;
using Kassyi.GSharp.Transpiler.Ast;

namespace Kassyi.GSharp.Transpiler
{
    public class CudaGenerator : IAstVisitor
    {
        private StringBuilder _sb = new StringBuilder();
        private Stack<string> _exprStack = new Stack<string>();

        public string Generate(BlockNode root)
        {
            _sb.Clear();
            _exprStack.Clear();
            
            Visit(root);
            
            return _sb.ToString();
        }

        public void Visit(BlockNode node)
        {
            foreach (var stmt in node.Statements)
            {
                stmt.Accept(this);
                if (_exprStack.Count > 0)
                {
                    string expr = _exprStack.Pop();
                    _sb.AppendLine($"{expr};");
                }
            }
        }

        public void Visit(ForLoopNode node)
        {
            node.Start.Accept(this);
            string start = _exprStack.Pop();
            
            node.End.Accept(this);
            string end = _exprStack.Pop();

            _sb.AppendLine($"for (int {node.IteratorName} = {start}; {node.IteratorName} < {end}; {node.IteratorName}++) {{");
            Visit(node.Body);
            _sb.AppendLine("}");
        }

        public void Visit(AssignNode node)
        {
            node.Value.Accept(this);
            string val = _exprStack.Pop();
            _sb.AppendLine($"{node.TargetName} = {val};");
        }

        public void Visit(BinaryOpNode node)
        {
            node.Left.Accept(this);
            string left = _exprStack.Pop();
            
            node.Right.Accept(this);
            string right = _exprStack.Pop();

            string opStr = node.Op switch
            {
                BinaryOperator.Add => "+",
                BinaryOperator.Sub => "-",
                BinaryOperator.Mul => "*",
                BinaryOperator.Div => "/",
                BinaryOperator.Mod => "%",
                BinaryOperator.BitAnd => "&",
                BinaryOperator.BitOr => "|",
                BinaryOperator.BitXor => "^",
                BinaryOperator.ShiftLeft => "<<",
                BinaryOperator.ShiftRight => ">>",
                BinaryOperator.Eq => "==",
                BinaryOperator.Neq => "!=",
                BinaryOperator.Lt => "<",
                BinaryOperator.Lte => "<=",
                BinaryOperator.Gt => ">",
                BinaryOperator.Gte => ">=",
                _ => throw new NotImplementedException()
            };

            _exprStack.Push($"({left} {opStr} {right})");
        }

        public void Visit(LiteralNode node)
        {
            _exprStack.Push(node.Value.ToString());
        }

        public void Visit(VariableNode node)
        {
            _exprStack.Push(node.Name);
        }

        public void Visit(MethodCallNode node)
        {
            var args = new List<string>();
            foreach (var arg in node.Arguments)
            {
                arg.Accept(this);
                args.Add(_exprStack.Pop());
            }
            
            string callStr = $"{node.MethodName}({string.Join(", ", args)})";
            
            // If it's a statement level call vs expression level call
            // For now, assume if _exprStack needs it, we push, else we appendLine.
            // A simple heuristic: AstBuilder pushes if return is not void.
            // Since we don't know here, we can just push it. If it was a statement, AssignNode/BlockNode would pop it.
            // Actually, BlockNode doesn't pop. So if a MethodCallNode is a direct child of BlockNode, it's a statement.
            // We can handle this by checking if it's the root of the visitor call, but let's just push it always and BlockNode can ignore it or we can add a flag.
            
            // For simplicity, we just push it. BlockNode needs to pop and write if it's a statement.
            _exprStack.Push(callStr);
        }
    }
}
