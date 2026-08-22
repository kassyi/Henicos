namespace Kassyi.GSharp.Transpiler.Ast;

public interface IAstNode
{
    void Accept(IAstVisitor visitor);
}

public interface IAstVisitor
{
    void Visit(BlockNode node);
    void Visit(ForLoopNode node);
    void Visit(WhileNode node);
    void Visit(IfNode node);
    void Visit(AssignNode node);
    void Visit(ArrayAssignNode node);
    void Visit(BinaryOpNode node);
    void Visit(LiteralNode node);
    void Visit(VariableNode node);
    void Visit(ArrayAccessNode node);
    void Visit(MethodCallNode node);
    void Visit(LabelNode node);
    void Visit(GotoNode node);
    void Visit(IfGotoNode node);
    void Visit(ReturnNode node);
}

public record BlockNode(IReadOnlyList<IAstNode> Statements) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public record ForLoopNode(string IteratorName, IAstNode Start, IAstNode End, BlockNode Body) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public record WhileNode(IAstNode Condition, BlockNode Body) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public record IfNode(IAstNode Condition, BlockNode TrueBlock, BlockNode? FalseBlock) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public record AssignNode(string TargetName, IAstNode Value) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public record ArrayAssignNode(string ArrayName, IAstNode Index, IAstNode Value) : IAstNode
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

public record ArrayAccessNode(string ArrayName, IAstNode Index) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public record MethodCallNode(string MethodName, IReadOnlyList<IAstNode> Arguments) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public record LabelNode(string Name) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public record GotoNode(string TargetName) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public record IfGotoNode(IAstNode Condition, string TrueTargetName) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}

public record ReturnNode(IAstNode? Value) : IAstNode
{
    public void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
