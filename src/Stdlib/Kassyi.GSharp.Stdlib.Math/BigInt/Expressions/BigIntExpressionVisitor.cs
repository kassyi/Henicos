using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

public ref struct BigIntExpressionVisitor(BigIntExecutionContext context, ScopedBigIntMemory targetResult = default)
{
    public BigIntExecutionContext Context = context;
    
    // An optional target buffer to use for the root evaluation
    private ScopedBigIntMemory _targetResult = targetResult;

    public ScopedBigIntMemory Visit(IGpuBigUIntExpression node)
    {
        var prev = _isRoot;
        _isRoot = false;
        var res = node.Accept(ref this);
        _isRoot = prev;
        return res;
    }
    private bool _isRoot = true;

    public ScopedBigIntMemory VisitGpuUIntBigInteger(GpuUIntBigInteger node)
    {
        // Wrapper without transferring ownership
        return new ScopedBigIntMemory(null, new BigIntMemoryHandle(new BigIntDeviceBuffer(node.DevicePointer, node.NumWords), 0, node.NumWords));
    }

    private ScopedBigIntMemory GetResultBuffer(int numWords, bool isRoot)
    {
        if (isRoot && !_targetResult.Handle.IsDefault)
        {
            var res = _targetResult;
            _targetResult = default;
            return res;
        }
        return Context.MemoryManager.AllocateTemp(numWords);
    }

    internal ScopedBigIntMemory VisitAdd(AddExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.Add(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitSub(SubExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.Sub(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitMul(MulExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.Mul(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitDiv(DivExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.Div(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitMod(ModExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.Mod(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitBitwiseAnd(BitwiseAndExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.BitwiseAnd(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitBitwiseOr(BitwiseOrExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.BitwiseOr(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitBitwiseXor(BitwiseXorExpression node)
    {
        using var left = Visit(node.Left);
        using var right = Visit(node.Right);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.BitwiseXor(ref Context, left, right, result);
        return result;
    }

    internal ScopedBigIntMemory VisitBitwiseNot(BitwiseNotExpression node)
    {
        using var target = Visit(node.Target);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.BitwiseNot(ref Context, target, result);
        return result;
    }

    internal ScopedBigIntMemory VisitShiftLeft(ShiftLeftExpression node)
    {
        using var target = Visit(node.Target);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.ShiftLeft(ref Context, target, node.ShiftBits, result);
        return result;
    }

    internal ScopedBigIntMemory VisitShiftRight(ShiftRightExpression node)
    {
        using var target = Visit(node.Target);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.ShiftRight(ref Context, target, node.ShiftBits, result);
        return result;
    }

    internal ScopedBigIntMemory VisitSqrt(SqrtExpression node)
    {
        using var target = Visit(node.Target);
        var result = GetResultBuffer(node.NumWords, _isRoot);
        Context.Arithmetic.Sqrt(ref Context, target, result);
        return result;
    }
}
