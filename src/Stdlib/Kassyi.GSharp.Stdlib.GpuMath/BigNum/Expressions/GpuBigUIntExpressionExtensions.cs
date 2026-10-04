namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using Runtime;

/// <summary>
/// Extension methods for evaluating GPU BigInteger expressions.
/// </summary>
public static class GpuBigUIntExpressionExtensions
{
    /// <summary>
    /// Evaluates this expression on the GPU into the specified target buffer using the Visitor pattern.
    /// </summary>
    public static GpuBigUInt Evaluate(this IGpuBigUIntExpression expr, GpuWorkspace workspace,
        GpuBigUInt? targetResult = null)
    {
        var execContext = new GpuBigIntExecutionContext(workspace, workspace.MemoryManager,
            new LegacyProviderArithmeticAdapter(), workspace.Stream);

        ScopedBigIntMemory targetMem = default;
        GpuBigUInt finalResult;

        if (targetResult != null)
        {
            var res = targetResult.Value;
            var dummyHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(res.DevicePointer, res.NumWords), 0,
                res.NumWords);
            targetMem = new ScopedBigIntMemory(null, dummyHandle);
            finalResult = res;
        }
        else
        {
            finalResult = workspace.RentTemp(expr.NumWords);
            var dummyHandle = new GpuBigIntMemoryHandle(
                new GpuBigIntDeviceBuffer(finalResult.DevicePointer, finalResult.NumWords),
                0, finalResult.NumWords);
            targetMem = new ScopedBigIntMemory(null, dummyHandle);
        }

        var visitor = new BigIntExpressionVisitor(execContext, targetMem);
        var scopedMem = expr.Accept(ref visitor);

        if (scopedMem.Handle.Buffer != null && scopedMem.Handle.Buffer.Pointer != finalResult.DevicePointer)
        {
            workspace.Provider.CopyDeviceToDevice(finalResult.DevicePointer, scopedMem.Handle.Buffer.Pointer,
                Math.Min(finalResult.NumWords, scopedMem.Handle.NumWords), workspace.Stream);
        }

        return finalResult;
    }
}
