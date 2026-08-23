
namespace Kassyi.GSharp.GpuBigInt.Expressions;
using Kassyi.GSharp.GpuBigInt.Kernels;
using Kassyi.GSharp.GpuBigInt.Contexts;
using Kassyi.GSharp.GpuBigInt.Interop;

public readonly struct GpuBigIntExpr
{
    public IGpuBigIntExpression Node
    {
        get;
    }

    internal GpuBigIntExpr(IGpuBigIntExpression node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
    }



    public static implicit operator GpuBigIntExpr(GpuBigInteger b)
        => new(b);

    public static GpuBigIntExpr operator +(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new AddExpression(left.Node, right.Node));

    public static GpuBigIntExpr operator -(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SubExpression(left.Node, right.Node));

    public static GpuBigIntExpr operator *(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new MulExpression(left.Node, right.Node));

    public static GpuBigIntExpr operator &(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new BitwiseAndExpression(left.Node, right.Node));

    public static GpuBigIntExpr operator <<(GpuBigIntExpr left, int shiftBits)
        => new(new ShiftLeftExpression(left.Node, shiftBits));

    public static GpuBigIntExpr operator >>(GpuBigIntExpr left, int shiftBits)
        => new(new ShiftRightExpression(left.Node, shiftBits));
}

