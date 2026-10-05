using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A stack-scoped visitor that recursively evaluates unsigned GPU BigInteger AST expressions using rented device memory buffers.
/// </summary>
/// <param name="context">The execution context providing memory manager and arithmetic providers.</param>
/// <param name="targetResult">An optional pre-allocated destination buffer to receive the root evaluation result.</param>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Traverses the expression AST and emits low-level kernel invocations, managing intermediate temporary buffer lifetimes
/// to prevent device memory leaks and eliminate managed heap allocations.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> As a <see langword="ref struct"/>, the visitor lives solely on the call stack.
/// </para>
/// </remarks>
public ref struct BigIntExpressionVisitor(GpuBigIntExecutionContext context, ScopedBigIntMemory targetResult = default)
{
    /// <summary>
    /// The execution context for memory allocation and kernel dispatch.
    /// </summary>
    public GpuBigIntExecutionContext Context = context;

    // An optional target buffer to use for the root evaluation
    private ScopedBigIntMemory _targetResult = targetResult;

    /// <summary>
    /// Visits and evaluates an arbitrary expression AST node.
    /// </summary>
    /// <param name="node">The expression node to visit.</param>
    /// <returns>A scoped memory segment holding the evaluated result.</returns>
    public ScopedBigIntMemory Visit(IGpuBigUIntExpression node)
    {
        var prev = _isRoot;
        _isRoot = false;
        var res = node.Accept(ref this);
        _isRoot = prev;
        return res;
    }

    private bool _isRoot = true;


    /// <summary>
    /// Visits a leaf constant GPU BigInteger and wraps it in a non-owning scoped memory handle.
    /// </summary>
    /// <param name="node">The GPU BigInteger node.</param>
    /// <returns>A scoped memory segment referencing the GPU BigInteger buffer.</returns>
    public ScopedBigIntMemory VisitGpuUIntBigInteger(GpuBigUInt node)
    {
        // Wrapper without transferring ownership
        return new ScopedBigIntMemory(null,
            new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(node.DevicePointer, node.NumWords), 0, node.NumWords));
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
