namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

using Runtime;

/// <summary>
/// Extension methods for evaluating GPU BigInteger expressions.
/// </summary>
public static class GpuBigUIntExpressionExtensions
{
    /// <summary>
    /// Evaluates this expression on the GPU into the specified target buffer using the Visitor pattern.
    /// </summary>
    public static GpuUIntBigInteger Evaluate(this IGpuBigUIntExpression expr, GpuContext context, GpuUIntBigInteger? targetResult = null)
    {
        var execContext = new BigIntExecutionContext(context, context.MemoryManager, new LegacyProviderArithmeticAdapter(), context.Stream);
        
        ScopedBigIntMemory targetMem = default;
        GpuUIntBigInteger finalResult;
        
        if (targetResult != null)
        {
            var res = targetResult.Value;
            var dummyHandle = new BigIntMemoryHandle(new BigIntDeviceBuffer(res.DevicePointer, res.NumWords), 0, res.NumWords);
            targetMem = new ScopedBigIntMemory(null, dummyHandle);
            finalResult = res;
        }
        else
        {
            finalResult = context.RentTemp(expr.NumWords);
            var dummyHandle = new BigIntMemoryHandle(
                new BigIntDeviceBuffer(finalResult.DevicePointer, finalResult.NumWords), 
                0, finalResult.NumWords);
            targetMem = new ScopedBigIntMemory(null, dummyHandle);
        }
        
        var visitor = new BigIntExpressionVisitor(execContext, targetMem);
        var scopedMem = expr.Accept(ref visitor);
        
        if (scopedMem.Handle.Buffer != null && scopedMem.Handle.Buffer.Pointer != finalResult.DevicePointer)
        {
            context.Provider.CopyDeviceToDevice(finalResult.DevicePointer, scopedMem.Handle.Buffer.Pointer, System.Math.Min(finalResult.NumWords, scopedMem.Handle.NumWords), context.Stream);
        }
        
        return finalResult;
    }
}