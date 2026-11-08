namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Provides extension methods for evaluating GPU BigInteger AST expressions.
/// </summary>
public static class GpuBigUIntExpressionExtensions
{
    /// <summary>
    /// Evaluates an arbitrary-precision integer expression AST on the GPU into a new or provided target buffer using the Visitor pattern.
    /// </summary>
    /// <param name="expr">The root expression AST node to evaluate.</param>
    /// <param name="workspace">The GPU workspace managing kernel parameters and memory pools.</param>
    /// <param name="targetResult">An optional destination buffer, at least as wide as the result
    /// (<see cref="IGpuBigUIntExpression.NumWords"/>); its words above the result are cleared. If
    /// <see langword="null"/>, a temporary buffer of the result's width is rented. It may be one of the
    /// expression's operands (<c>x.Value = x / y</c>): the root then computes into a temporary and copies the result
    /// into it.</param>
    /// <returns>A <see cref="GpuBigUInt"/> containing the evaluated unsigned large integer.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="targetResult"/> has fewer words than the expression; nothing is written. Narrow the expression
    /// with <see cref="GpuBigUIntExpr.Fit"/> or <see cref="GpuBigUIntExpr.Truncate"/>.
    /// </exception>
    /// <exception cref="OverflowException">
    /// CPU fallback mode only: a subtraction is below zero, or <see cref="GpuBigUIntExpr.Fit"/> does not fit. On the
    /// GPU these domain errors are reported at the next read of the workspace stream.
    /// </exception>
    public static GpuBigUInt Evaluate(this IGpuBigUIntExpression expr, GpuWorkspace workspace,
        GpuBigUInt? targetResult = null)
    {
        var numWords = expr.NumWords;
        if (targetResult is { } target && target.NumWords < numWords)
        {
            throw new ArgumentException(
                $"Destination buffer must have at least {numWords} words; it has {target.NumWords}. Narrow the " +
                "expression with Fit or Truncate.",
                nameof(targetResult)
            );
        }

        var execContext = GpuWorkspaceArithmetic.CreateContext(workspace);

        ScopedBigIntMemory targetMem;
        GpuBigUInt finalResult;

        if (targetResult != null)
        {
            var res = targetResult.Value;
            targetMem = new ScopedBigIntMemory(res.Handle.Borrow());
            finalResult = res;
        }
        else
        {
            finalResult = workspace.RentTemp(numWords);
            targetMem = new ScopedBigIntMemory(finalResult.Handle.Borrow());
        }

        var visitor = new BigIntExpressionVisitor(execContext, targetMem);

        // A temporary the root used (its target overlapped an operand) goes back to the pool after the copy; the target
        // itself and a leaf are borrowed, so their Dispose does nothing
        using var scopedMem = expr.Accept(ref visitor);
        if (!scopedMem.Handle.IsDefault && scopedMem.Handle.DevicePointer != finalResult.DevicePointer)
        {
            var words = Math.Min(finalResult.NumWords, scopedMem.Handle.NumWords);
            if (DeviceRanges.Overlaps(finalResult.DevicePointer, words, scopedMem.Handle.DevicePointer, words))
            {
                // A leaf root that partly overlaps the target: cuMemcpyDtoD does not promise overlapping copies
                using var staging = new GpuBigUInt(workspace, words);
                workspace.Provider.CopyDeviceToDevice(staging.DevicePointer, scopedMem.Handle.DevicePointer, words,
                    workspace.Stream);
                workspace.Provider.CopyDeviceToDevice(finalResult.DevicePointer, staging.DevicePointer, words,
                    workspace.Stream);
            }
            else
            {
                workspace.Provider.CopyDeviceToDevice(finalResult.DevicePointer, scopedMem.Handle.DevicePointer, words,
                    workspace.Stream);
            }
        }

        // The root wrote its own width; the words of a wider target above it would keep their old value
        SignedRoot.ClearAbove(finalResult, numWords);
        return finalResult;
    }
}
