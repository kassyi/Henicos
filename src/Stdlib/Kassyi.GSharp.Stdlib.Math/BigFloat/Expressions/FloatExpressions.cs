namespace Kassyi.GSharp.Stdlib.Math.BigFloat.Expressions;

public interface IGpuBigFloatExpression
{
    GpuBigFloat Accept(ref BigFloatExpressionVisitor visitor);
}

public interface IGpuBigUFloatExpression
{
    GpuBigUFloat Accept(ref BigFloatExpressionVisitor visitor);
}

public readonly struct GpuBigFloatAddExpr(GpuBigFloat left, GpuBigFloat right) : IGpuBigFloatExpression
{
    public readonly GpuBigFloat Left = left;
    public readonly GpuBigFloat Right = right;

    public GpuBigFloat Accept(ref BigFloatExpressionVisitor visitor) => visitor.VisitAdd(this);
}

public readonly struct GpuBigFloatSubExpr(GpuBigFloat left, GpuBigFloat right) : IGpuBigFloatExpression
{
    public readonly GpuBigFloat Left = left;
    public readonly GpuBigFloat Right = right;

    public GpuBigFloat Accept(ref BigFloatExpressionVisitor visitor) => visitor.VisitSub(this);
}

public readonly struct GpuBigFloatMulExpr(GpuBigFloat left, GpuBigFloat right) : IGpuBigFloatExpression
{
    public readonly GpuBigFloat Left = left;
    public readonly GpuBigFloat Right = right;

    public GpuBigFloat Accept(ref BigFloatExpressionVisitor visitor) => visitor.VisitMul(this);
}

public readonly struct GpuBigUFloatAddExpr(GpuBigUFloat left, GpuBigUFloat right) : IGpuBigUFloatExpression
{
    public readonly GpuBigUFloat Left = left;
    public readonly GpuBigUFloat Right = right;

    public GpuBigUFloat Accept(ref BigFloatExpressionVisitor visitor) => visitor.VisitAdd(this);
}

public readonly struct GpuBigUFloatSubExpr(GpuBigUFloat left, GpuBigUFloat right) : IGpuBigUFloatExpression
{
    public readonly GpuBigUFloat Left = left;
    public readonly GpuBigUFloat Right = right;

    public GpuBigUFloat Accept(ref BigFloatExpressionVisitor visitor) => visitor.VisitSub(this);
}
