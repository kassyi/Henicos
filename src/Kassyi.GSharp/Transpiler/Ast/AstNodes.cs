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

    public record BlockNode(IReadOnlyList<IAstNode> Statements) : IAstNode
    {
        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public record ForLoopNode(string IteratorName, IAstNode Start, IAstNode End, BlockNode Body) : IAstNode
    {
        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public record AssignNode(string TargetName, IAstNode Value) : IAstNode
    {
        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public enum BinaryOperator
    {
        Add, Sub, Mul, Div, Mod,
        BitAnd, BitOr, BitXor, ShiftLeft, ShiftRight,
        Eq, Neq, Lt, Lte, Gt, Gte
    }

    public record BinaryOpNode(BinaryOperator Op, IAstNode Left, IAstNode Right) : IAstNode
    {
        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public record LiteralNode(object Value) : IAstNode
    {
        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public record VariableNode(string Name) : IAstNode
    {
        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }

    public record MethodCallNode(string MethodName, IReadOnlyList<IAstNode> Arguments) : IAstNode
    {
        public void Accept(IAstVisitor visitor) => visitor.Visit(this);
    }
}
