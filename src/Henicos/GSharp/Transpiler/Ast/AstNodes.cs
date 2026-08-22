using System.Collections.Generic;

namespace Kassyi.GSharp.Transpiler.Ast
{
    public interface IAstNode
    {
        void Accept(IAstVisitor visitor);
    }

    public interface IAstVisitor
    {
        void Visit(BlockNode node);
        void Visit(ForLoopNode node);
        void Visit(AssignNode node);
        void Visit(BinaryOpNode node);
        void Visit(LiteralNode node);
        void Visit(VariableNode node);
        void Visit(MethodCallNode node);
    }

    public class BlockNode : IAstNode
    {
        public List<IAstNode> Statements { get; set; } = new List<IAstNode>();

        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public class ForLoopNode : IAstNode
    {
        public string IteratorName { get; set; }
        public IAstNode Start { get; set; }
        public IAstNode End { get; set; }
        public BlockNode Body { get; set; }

        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public class AssignNode : IAstNode
    {
        public string TargetName { get; set; }
        public IAstNode Value { get; set; }

        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public enum BinaryOperator
    {
        Add, Sub, Mul, Div, Mod,
        BitAnd, BitOr, BitXor, ShiftLeft, ShiftRight,
        Eq, Neq, Lt, Lte, Gt, Gte
    }

    public class BinaryOpNode : IAstNode
    {
        public BinaryOperator Op { get; set; }
        public IAstNode Left { get; set; }
        public IAstNode Right { get; set; }

        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public class LiteralNode : IAstNode
    {
        public object Value { get; set; } // int, long, etc.

        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public class VariableNode : IAstNode
    {
        public string Name { get; set; }

        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public class MethodCallNode : IAstNode
    {
        public string MethodName { get; set; }
        public List<IAstNode> Arguments { get; set; } = new List<IAstNode>();

        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }
}
