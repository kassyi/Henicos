namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The low-level evaluation of vector expressions: by plain recursion on the caller's stack, one call stack frame per
/// level of vector nodes, for an expression the caller knows to be shallow.
/// </summary>
/// <remarks>
/// <para>
/// <c>Evaluate</c> of a vector expression evaluates any depth: it recurses through the first levels and walks the
/// levels below them with explicit stacks, which costs a few tens of nanoseconds of host time per node. The methods of
/// this class recurse through every level instead, as fast as a hand-written recursion, and support at most
/// <see cref="MaxCallStackDepth"/> levels of vector nodes. A level takes a few kilobytes of stack, so the supported
/// depth fits in about 256 KB, well inside the default stack of a thread (1 MB on Windows, 8 MB on Linux).
/// </para>
/// <para>
/// An expression deeper than <see cref="MaxCallStackDepth"/> levels throws <see cref="NotSupportedException"/> when
/// the evaluation reaches the first node below that depth; the nodes evaluated before it keep their buffers, as after
/// any failed evaluation. The results, their order of evaluation and the checks are those of <c>Evaluate</c>.
/// </para>
/// </remarks>
public static class VectorExpressionRecursion
{
    /// <summary>The most levels of vector nodes evaluated by recursion on the caller's stack.</summary>
    public const int MaxCallStackDepth = 64;

    /// <summary>
    /// Evaluates <paramref name="expression"/> by recursion on the caller's stack; the same result as
    /// <see cref="IGpuBigIntVectorExpression.Evaluate"/>.
    /// </summary>
    /// <param name="expression">The expression, at most <see cref="MaxCallStackDepth"/> levels deep.</param>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="elementOffset">The starting element index to evaluate.</param>
    /// <param name="elementCount">The number of elements to evaluate, or <see langword="null"/> for the rest.</param>
    /// <param name="targetResult">
    /// An optional destination, as for <see cref="IGpuBigIntVectorExpression.Evaluate"/>.
    /// </param>
    /// <returns>The evaluated elements, as <see cref="IGpuBigIntVectorExpression.Evaluate"/> returns them.</returns>
    /// <exception cref="NotSupportedException">
    /// The expression is deeper than <see cref="MaxCallStackDepth"/> levels, or the workspace uses the CPU fallback.
    /// </exception>
    public static GpuBigIntArrayView EvaluateOnCallStack(this IGpuBigIntVectorExpression expression,
        GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArrayView targetResult = default)
    {
        DeviceCounted.ThrowIfCpuFallback(workspace);
        var request = new VectorRequest(elementOffset, elementCount, VectorStorage.Of(targetResult));
        return DeviceCounted.Materialize(workspace, VectorWalk.EvaluateDirect(expression, workspace, request, 0).Int);
    }

    /// <summary>
    /// Evaluates <paramref name="expression"/> by recursion on the caller's stack; the same result as
    /// <see cref="IGpuBigFloatVectorExpression.Evaluate"/>.
    /// </summary>
    /// <param name="expression">The expression, at most <see cref="MaxCallStackDepth"/> levels deep.</param>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="elementOffset">The starting element index to evaluate.</param>
    /// <param name="elementCount">The number of elements to evaluate, or <see langword="null"/> for the rest.</param>
    /// <param name="targetResult">
    /// An optional destination, as for <see cref="IGpuBigFloatVectorExpression.Evaluate"/>.
    /// </param>
    /// <returns>The evaluated elements, as <see cref="IGpuBigFloatVectorExpression.Evaluate"/> returns them.</returns>
    /// <exception cref="NotSupportedException">
    /// The expression is deeper than <see cref="MaxCallStackDepth"/> levels, or the workspace uses the CPU fallback.
    /// </exception>
    public static GpuBigFloatArrayView EvaluateOnCallStack(this IGpuBigFloatVectorExpression expression,
        GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigFloatArrayView targetResult = default)
    {
        DeviceCounted.ThrowIfCpuFallback(workspace);
        var request = new VectorRequest(elementOffset, elementCount, VectorStorage.Of(targetResult));
        return DeviceCounted.Materialize(workspace,
            VectorWalk.EvaluateDirect(expression, workspace, request, 0).Float);
    }

    /// <summary>
    /// Evaluates <paramref name="expression"/> by recursion on the caller's stack; the same result as
    /// <see cref="IGpuBoolVectorExpression.Evaluate"/>.
    /// </summary>
    /// <param name="expression">The expression, at most <see cref="MaxCallStackDepth"/> levels deep.</param>
    /// <param name="workspace">The GPU workspace.</param>
    /// <returns>The flags, as <see cref="IGpuBoolVectorExpression.Evaluate"/> returns them.</returns>
    /// <exception cref="NotSupportedException">
    /// The expression is deeper than <see cref="MaxCallStackDepth"/> levels, or the workspace uses the CPU fallback.
    /// </exception>
    public static GpuBigUIntArrayView EvaluateOnCallStack(this IGpuBoolVectorExpression expression,
        GpuWorkspace workspace)
    {
        DeviceCounted.ThrowIfCpuFallback(workspace);
        return DeviceCounted.Materialize(workspace,
            VectorWalk.EvaluateDirect(expression, workspace, VectorRequest.Whole, 0).Bool);
    }
}
