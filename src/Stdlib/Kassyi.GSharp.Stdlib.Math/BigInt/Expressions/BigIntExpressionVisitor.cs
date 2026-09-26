using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

public ref struct BigIntExpressionVisitor(BigIntExecutionContext context, ScopedBigIntMemory targetResult = default)
{
    public BigIntExecutionContext Context = context;
    
    // An optional target buffer to use for the root evaluation
    private ScopedBigIntMemory _targetResult = targetResult;

    public ScopedBigIntMemory Visit(IGpuBigUIntExpression node) => node.Accept(ref this);

    public ScopedBigIntMemory VisitGpuUIntBigInteger(GpuUIntBigInteger node)
    {
        // Wrapper without transferring ownership
        return new ScopedBigIntMemory(null, new BigIntMemoryHandle(new BigIntDeviceBuffer(node.DevicePointer, node.NumWords), 0, node.NumWords));
    }

    private ScopedBigIntMemory GetResultBuffer(int numWords)
    {
        if (_targetResult.Handle.IsDefault)
        {
            return Context.MemoryManager.AllocateTemp(numWords);
        }

        var res = _targetResult;
        _targetResult = default; 
        return res;
    }

    internal ScopedBigIntMemory VisitAdd(AddExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.Add(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitSub(SubExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.Sub(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitMul(MulExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.Mul(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitDiv(DivExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.Div(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitMod(ModExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.Mod(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitBitwiseAnd(BitwiseAndExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.BitwiseAnd(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitBitwiseOr(BitwiseOrExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.BitwiseOr(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitBitwiseXor(BitwiseXorExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.BitwiseXor(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitBitwiseNot(BitwiseNotExpression node)
    {
        using var target = Visit(node.Target);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.BitwiseNot(ref Context, target, result);
        return result;
    }

    internal ScopedBigIntMemory VisitShiftLeft(ShiftLeftExpression node)
    {
        using var target = Visit(node.Target);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.ShiftLeft(ref Context, target, node.ShiftBits, result);
        return result;
    }

    internal ScopedBigIntMemory VisitShiftRight(ShiftRightExpression node)
    {
        using var target = Visit(node.Target);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.ShiftRight(ref Context, target, node.ShiftBits, result);
        return result;
    }

    internal ScopedBigIntMemory VisitSqrt(SqrtExpression node)
    {
        using var target = Visit(node.Target);
        var result = GetResultBuffer(node.NumWords);
        Context.Arithmetic.Sqrt(ref Context, target, result);
        return result;
    }
}
