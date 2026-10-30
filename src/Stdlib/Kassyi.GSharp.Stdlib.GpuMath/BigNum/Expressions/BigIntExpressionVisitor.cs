

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
        return new ScopedBigIntMemory(node.Handle.Borrow());
    }

    // The root writes into the target, unless the target overlaps one of the root's operands: the kernels take their
    // pointers as __restrict__ and some read words that other threads write, so the root then computes into a
    // temporary, which the caller copies into the target. Only the root can meet the target: every other node writes
    // a temporary, and the root reads only its own operands.
    private ScopedBigIntMemory GetResultBuffer(int numWords, bool isRoot, GpuBigIntMemoryHandle operand0,
        GpuBigIntMemoryHandle operand1 = default)
    {
        if (isRoot && !_targetResult.Handle.IsDefault)
        {
            var target = _targetResult;
            _targetResult = default;
            if (!DeviceRanges.Overlaps(target.Handle, operand0) && !DeviceRanges.Overlaps(target.Handle, operand1))
            {
                return target;
            }
        }

        return Context.MemoryManager.AllocateTemp(numWords);
    }

    private enum BinaryOp
    {
        Add,
        Sub,
        Mul,
        Div,
        Mod,
        And,
        Or,
        Xor
    }

    private ScopedBigIntMemory VisitBinaryOperation(IGpuBigUIntExpression leftNode, IGpuBigUIntExpression rightNode,
        int numWords, BinaryOp op)
    {
        using var left = Visit(leftNode);
        using var right = Visit(rightNode);
        var result = GetResultBuffer(numWords, _isRoot, left.Handle, right.Handle);

        switch (op)
        {
            case BinaryOp.Add: Context.Arithmetic.Add(ref Context, left, right, result); break;
            case BinaryOp.Sub: Context.Arithmetic.Sub(ref Context, left, right, result); break;
            case BinaryOp.Mul: Context.Arithmetic.Mul(ref Context, left, right, result); break;
            case BinaryOp.Div: Context.Arithmetic.Div(ref Context, left, right, result); break;
            case BinaryOp.Mod: Context.Arithmetic.Mod(ref Context, left, right, result); break;
            case BinaryOp.And: Context.Arithmetic.BitwiseAnd(ref Context, left, right, result); break;
            case BinaryOp.Or: Context.Arithmetic.BitwiseOr(ref Context, left, right, result); break;
            case BinaryOp.Xor: Context.Arithmetic.BitwiseXor(ref Context, left, right, result); break;
        }

        return result;
    }

    internal ScopedBigIntMemory VisitAdd(AddExpression node) =>
        VisitBinaryOperation(node.Left, node.Right, node.NumWords, BinaryOp.Add);

    internal ScopedBigIntMemory VisitSub(SubExpression node) =>
        VisitBinaryOperation(node.Left, node.Right, node.NumWords, BinaryOp.Sub);

    internal ScopedBigIntMemory VisitMul(MulExpression node) =>
        VisitBinaryOperation(node.Left, node.Right, node.NumWords, BinaryOp.Mul);

    internal ScopedBigIntMemory VisitDiv(DivExpression node) =>
        VisitBinaryOperation(node.Left, node.Right, node.NumWords, BinaryOp.Div);

    internal ScopedBigIntMemory VisitMod(ModExpression node) =>
        VisitBinaryOperation(node.Left, node.Right, node.NumWords, BinaryOp.Mod);

    internal ScopedBigIntMemory VisitBitwiseAnd(BitwiseAndExpression node) =>
        VisitBinaryOperation(node.Left, node.Right, node.NumWords, BinaryOp.And);

    internal ScopedBigIntMemory VisitBitwiseOr(BitwiseOrExpression node) =>
        VisitBinaryOperation(node.Left, node.Right, node.NumWords, BinaryOp.Or);

    internal ScopedBigIntMemory VisitBitwiseXor(BitwiseXorExpression node) =>
        VisitBinaryOperation(node.Left, node.Right, node.NumWords, BinaryOp.Xor);

    private enum UnaryOp
    {
        Not,
        Sqrt
    }

    private ScopedBigIntMemory VisitUnaryOperation(IGpuBigUIntExpression targetNode, int numWords, UnaryOp op)
    {
        using var target = Visit(targetNode);
        var result = GetResultBuffer(numWords, _isRoot, target.Handle);

        switch (op)
        {
            case UnaryOp.Not: Context.Arithmetic.BitwiseNot(ref Context, target, result); break;
            case UnaryOp.Sqrt: Context.Arithmetic.Sqrt(ref Context, target, result); break;
        }

        return result;
    }

    internal ScopedBigIntMemory VisitBitwiseNot(BitwiseNotExpression node) =>
        VisitUnaryOperation(node.Target, node.NumWords, UnaryOp.Not);

    internal ScopedBigIntMemory VisitSqrt(SqrtExpression node) =>
        VisitUnaryOperation(node.Target, node.NumWords, UnaryOp.Sqrt);

    private enum ShiftOp
    {
        Left,
        Right
    }

    private ScopedBigIntMemory VisitShiftOperation(IGpuBigUIntExpression targetNode, int numWords, int shiftBits,
        ShiftOp op)
    {
        using var target = Visit(targetNode);
        var result = GetResultBuffer(numWords, _isRoot, target.Handle);

        switch (op)
        {
            case ShiftOp.Left: Context.Arithmetic.ShiftLeft(ref Context, target, shiftBits, result); break;
            case ShiftOp.Right: Context.Arithmetic.ShiftRight(ref Context, target, shiftBits, result); break;
        }

        return result;
    }

    internal ScopedBigIntMemory VisitShiftLeft(ShiftLeftExpression node) =>
        VisitShiftOperation(node.Target, node.NumWords, node.ShiftBits, ShiftOp.Left);

    internal ScopedBigIntMemory VisitShiftRight(ShiftRightExpression node) =>
        VisitShiftOperation(node.Target, node.NumWords, node.ShiftBits, ShiftOp.Right);
}
