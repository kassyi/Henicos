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
    /// <param name="targetResult">An optional destination buffer. If <see langword="null"/>, a temporary buffer is rented.</param>
    /// <returns>A <see cref="GpuBigUInt"/> containing the evaluated unsigned large integer.</returns>
    public static GpuBigUInt Evaluate(this IGpuBigUIntExpression expr, GpuWorkspace workspace,
        GpuBigUInt? targetResult = null)
    {

        var execContext = new GpuBigIntExecutionContext(workspace, workspace.MemoryManager,
            new LegacyProviderArithmeticAdapter(), workspace.Stream);

        ScopedBigIntMemory targetMem;
        GpuBigUInt finalResult;

        if (targetResult != null)
        {
            var res = targetResult.Value;
            targetMem = new ScopedBigIntMemory(null, res.Handle.Borrow());
            finalResult = res;
        }
        else
        {
            finalResult = workspace.RentTemp(expr.NumWords);
            targetMem = new ScopedBigIntMemory(null, finalResult.Handle.Borrow());
        }

        var visitor = new BigIntExpressionVisitor(execContext, targetMem);
        var scopedMem = expr.Accept(ref visitor);

        if (!scopedMem.Handle.IsDefault && scopedMem.Handle.DevicePointer != finalResult.DevicePointer)
        {
            workspace.Provider.CopyDeviceToDevice(finalResult.DevicePointer, scopedMem.Handle.DevicePointer,
                Math.Min(finalResult.NumWords, scopedMem.Handle.NumWords), workspace.Stream);
        }

        return finalResult;
    }
}
