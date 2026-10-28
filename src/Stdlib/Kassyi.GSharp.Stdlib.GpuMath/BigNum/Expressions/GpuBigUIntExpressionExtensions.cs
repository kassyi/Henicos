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
    /// <param name="targetResult">An optional destination buffer. If <see langword="null"/>, a temporary buffer is rented.
    /// It may be one of the expression's operands (<c>x.Value = x / y</c>): the root then computes into a temporary
    /// and copies the result into it.</param>
    /// <returns>A <see cref="GpuBigUInt"/> containing the evaluated unsigned large integer.</returns>
    public static GpuBigUInt Evaluate(this IGpuBigUIntExpression expr, GpuWorkspace workspace,
        GpuBigUInt? targetResult = null)
    {

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
            finalResult = workspace.RentTemp(expr.NumWords);
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

        return finalResult;
    }
}
